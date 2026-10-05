import { defineConfig } from 'wxt';

export default defineConfig({
  modules: ['@wxt-dev/module-react'],
  manifest: {
    name: 'Qtranslator',
    description: 'A lightweight local-first translation companion for English learning.',
    version: '1.2.1',
    key: 'MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAuV9XWahRTIpSVh/koHAPrpy1JEc97sB0xPx1UTDCoCc6ig/T40iWlGPx7iOncvIHlIytyun4459/hl7lG7mj1Oi4WIP8LHMOGxPr7pK4tMoYL28NMe51Kk3dNOYxgjbN7Fq+vf7h9xA5tD8vHdiuiw5nl2XQEeDYELpKAzFpttenl5rQFZUH+xNkbj7hjRqug5jTGPtdLOJ4EePFKKTsBkpwNytGqITgqxUovYWBlncXkJtuWGPo8hkm3xNNYIi8Xybmy6FncxclfGMf+erNv/r5/M6j6+aXY7RqGfj2OemGxKfbsk8Wpz9RU57An6wWMr6Ih8HbvjC8Lf/3aeml9wIDAQAB',
    permissions: ['contextMenus', 'sidePanel', 'storage', 'nativeMessaging', 'offscreen'],
    host_permissions: [
      'https://api.cognitive.microsofttranslator.com/*',
      'https://api.dictionaryapi.dev/*',
      'https://api.datamuse.com/*',
    ],
    action: {
      default_title: 'Open Qtranslator',
    },
    side_panel: {
      default_path: 'sidepanel.html',
    },
    options_ui: {
      page: 'options.html',
      open_in_tab: true,
    },
  },
});
