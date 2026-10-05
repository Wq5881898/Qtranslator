import { access, readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const manifestPath = '.output/chrome-mv3/manifest.json';
const requiredFiles = [
  manifestPath,
  '.output/chrome-mv3/sidepanel.html',
  '.output/chrome-mv3/options.html',
  '.output/chrome-mv3/offscreen.html',
  'PRIVACY.md',
  'THIRD_PARTY_NOTICES.md',
  'CHANGELOG.md',
  'docs/TEST_REPORT_STAGE_1.md',
];

await Promise.all(requiredFiles.map((path) => access(path)));

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const packageMetadata = JSON.parse(await readFile('package.json', 'utf8'));
const expectedPermissions = ['contextMenus', 'sidePanel', 'storage', 'nativeMessaging', 'offscreen'];
const actualPermissions = [...(manifest.permissions ?? [])].sort();

if (JSON.stringify(actualPermissions) !== JSON.stringify([...expectedPermissions].sort())) {
  throw new Error(`Unexpected permissions: ${actualPermissions.join(', ')}`);
}

const expectedHosts = [
  'https://api.cognitive.microsofttranslator.com/*',
  'https://api.dictionaryapi.dev/*',
  'https://api.datamuse.com/*',
];
const actualHosts = [...(manifest.host_permissions ?? [])].sort();

if (JSON.stringify(actualHosts) !== JSON.stringify([...expectedHosts].sort())) {
  throw new Error(`Unexpected host permissions: ${actualHosts.join(', ')}`);
}

if (manifest.version !== packageMetadata.version) {
  throw new Error(`Unexpected release version: ${manifest.version}`);
}

if (!manifest.key) {
  throw new Error('Stage 2 build must contain a stable extension key for Native Messaging.');
}

const keyBytes = Buffer.from(manifest.key, 'base64');
const keyHash = createHash('sha256').update(keyBytes).digest();
const alphabet = 'abcdefghijklmnop';
let extensionId = '';
for (const value of keyHash.subarray(0, 16)) {
  extensionId += alphabet[value >> 4] + alphabet[value & 15];
}
if (extensionId !== 'kjgoachjhbfkijaopcmallakpfnalkdo') {
  throw new Error(`Unexpected Stage 2 extension ID: ${extensionId}`);
}

console.log('Release artifact, permissions, hosts, version, and required documents verified.');
