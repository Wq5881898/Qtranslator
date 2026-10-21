using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using Qtranslator.Core;
using Qtranslator.Desktop;

internal static class BridgeValidation
{
    public static async Task<int> RunAsync(string hostPath)
    {
        var previousPipe = Environment.GetEnvironmentVariable(BridgeEnvelope.PipeNameEnvironmentVariable);
        var pipeName = "qtranslator-validation-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(BridgeEnvelope.PipeNameEnvironmentVariable, pipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        try
        {
            var json = JsonSerializer.Serialize(new { name = "com.wq5881898.qtranslator", type = "stdio", path = hostPath,
                allowed_origins = new[] { "chrome-extension://kjgoachjhbfkijaopcmallakpfnalkdo/" } });
            Assert(BridgeRegistrationService.IsValidManifest(json, hostPath), "Valid registration rejected");
            Assert(!BridgeRegistrationService.IsValidManifest("{", hostPath), "Truncated JSON accepted");
            Assert(!BridgeRegistrationService.IsValidManifest("{}", hostPath), "Missing manifest fields accepted");
            Assert(!BridgeRegistrationService.IsValidManifest(json.Replace("kjgoachjhbfkijaopcmallakpfnalkdo", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), hostPath), "Wrong extension accepted");
            Assert(!BridgeRegistrationService.IsValidManifest(json, hostPath + ".missing"), "Missing host accepted");
            Console.WriteLine("PASS registration validation: valid, malformed, incomplete, wrong extension, missing binary");

            using var host = Process.Start(new ProcessStartInfo(hostPath) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            }) ?? throw new Exception("Host could not start");
            var stderr = host.StandardError.ReadToEndAsync(token);
            var input = host.StandardInput.BaseStream;
            var output = host.StandardOutput.BaseStream;
            try
            {
                // Test the actual desktop provider against the real host. Respond as
                // the extension, rather than mistaking a host-only health for a full round trip.
                // Start with a desktop request before Chrome has sent any input:
                // older extensions do not send a startup probe.
                var provider = new BrowserBridgeTranslationProvider();
                var health = provider.CheckHealthAsync(token);
                var request = await ReadAsync(output, token);
                await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("bridge.health.result", request.RequestId,
                    new { available = true, extension = "qtranslator" }), token);
                Assert((await health).IsAvailable, "Desktop-to-extension round trip failed");

                await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("bridge.health", "extension-health", new { }), token);
                Assert((await ReadAsync(output, token)).RequestId == "extension-health", "Initial health failed");

                var wrongReply = provider.CheckHealthAsync(token);
                request = await ReadAsync(output, token);
                await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("bridge.health.result", request.RequestId,
                    new { available = true, host = "qtranslator-bridge" }), token);
                Assert(!(await wrongReply).IsAvailable, "Host-only response passed as extension connection");
                Console.WriteLine("PASS desktop provider requires extension acknowledgement");

                await using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await pipe.ConnectAsync(token);
                    await NativeMessageFraming.WriteAsync(pipe, BridgeEnvelope.Create("translation.request", "desktop-translation", new { text = "hello" }), token);
                    Assert((await ReadAsync(output, token)).RequestId == "desktop-translation", "Translation not forwarded");
                    // A manual plugin check arrives while the desktop is waiting.
                    await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("bridge.health", "parallel-health", new { }), token);
                    var response = await ReadAsync(output, token);
                    Assert(response.RequestId == "parallel-health" && response.MessageType == "bridge.health.result", "Health probe was confused with a translation response");
                    await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("translation.result", "unrelated", new { translatedText = "wrong" }), token);
                    await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("translation.result", "desktop-translation", new { translatedText = "你好" }), token);
                    var translated = await ReadAsync(pipe, token);
                    Assert(translated.RequestId == "desktop-translation" && translated.Payload.GetProperty("translatedText").GetString() == "你好", "Wrong translation response routed to desktop");
                }
                Console.WriteLine("PASS concurrent health and translation; unrelated responses ignored");

                // Disconnect a desktop mid-request, then ensure the persistent host survives.
                await using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    await pipe.ConnectAsync(token);
                    await NativeMessageFraming.WriteAsync(pipe, BridgeEnvelope.Create("bridge.health", "abandoned", new { }), token);
                    await ReadAsync(output, token);
                }
                await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("bridge.health.result", "abandoned", new { available = true, extension = "qtranslator" }), token);
                var recovered = provider.CheckHealthAsync(token);
                request = await ReadAsync(output, token);
                await NativeMessageFraming.WriteAsync(input, BridgeEnvelope.Create("bridge.health.result", request.RequestId,
                    new { available = true, extension = "qtranslator" }), token);
                Assert((await recovered).IsAvailable, "Host did not recover from a disconnected desktop");
                Console.WriteLine("PASS desktop disconnection recovery");
            }
            finally
            {
                host.StandardInput.Close();
                await host.WaitForExitAsync(token);
            }
            Assert(host.ExitCode == 0, "Host exited with failure: " + await stderr);
            Console.WriteLine("PASS clean native-host shutdown");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Environment.SetEnvironmentVariable(BridgeEnvelope.PipeNameEnvironmentVariable, previousPipe); }
    }

    private static async Task<BridgeEnvelope> ReadAsync(Stream stream, CancellationToken token) =>
        await NativeMessageFraming.ReadAsync(stream, token) ?? throw new Exception("Unexpected end of stream");
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
