import { readFile, access } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const read = (path) => readFile(path, 'utf8');
const manifest = JSON.parse(await read('.output/chrome-mv3/manifest.json'));
const id = [...createHash('sha256').update(Buffer.from(manifest.key, 'base64')).digest().subarray(0, 16)]
  .map(b => 'abcdefghijklmnop'[b >> 4] + 'abcdefghijklmnop'[b & 15]).join('');
if (id === 'djbkcmlpogpnafgifiocehmkkghnhjjb' || manifest.name !== 'Qtranslator') throw new Error('Extension is not isolated.');
const desktop = 'desktop/Qtranslator.Desktop/';
const registration = await read(desktop + 'BridgeRegistrationService.cs');
const client = await read('src/core/stage2-bridge.ts');
const protocol = await read('desktop/Qtranslator.Core/BridgeProtocol.cs');
const favorites = await read('desktop/Qtranslator.Core/Favorites.cs');
const shortcuts = await read(desktop + 'ShortcutSettings.cs');
const log = await read(desktop + 'DesktopErrorLog.cs');
const installer = await read('installer/Qtranslator.iss');
const project = await read(desktop + 'Qtranslator.Desktop.csproj');
function requireText(text, required, name) {
  if (!required.every(value => text.includes(value))) throw new Error('Missing isolated identity in ' + name);
}
requireText(registration, ['com.wq5881898.qtranslator', id, '"Qtranslator"', 'Qtranslator.BridgeHost.exe'], 'bridge registration');
requireText(client, ['com.wq5881898.qtranslator'], 'extension native host');
requireText(protocol, ['wq5881898.qtranslator', 'QTRANSLATOR_BRIDGE_PIPE_NAME'], 'named pipe');
requireText(favorites, ['"Qtranslator"', 'Wq5881898.Qtranslator.Favorites'], 'favorites');
requireText(shortcuts, ['"Qtranslator"', 'new(ShortcutModifiers.ControlAlt, "X")'], 'shortcut storage');
requireText(log, ['"Qtranslator"'], 'log directory');
requireText(project, ['<AssemblyName>Qtranslator</AssemblyName>'], 'executable filename');
requireText(installer, ['D9373B62-6B6A-4BFE-9728-398AE02878EA', 'Programs\\Qtranslator', 'Qtranslator.exe', id, 'com.wq5881898.qtranslator'], 'installer');
for (const [name, text] of Object.entries({ registration, client, protocol, favorites, log, installer })) {
  if (/com\.wq5881898\.translator\.stage2|wq5881898\.translator\.stage2|"Translator"|\\Programs\\Translator|\/im Translator\./.test(text))
    throw new Error('Legacy runtime identity remains in ' + name);
}
await access('desktop/Qtranslator.slnx');
console.log(`Qtranslator identity isolation verified; independent extension ID: ${id}`);
