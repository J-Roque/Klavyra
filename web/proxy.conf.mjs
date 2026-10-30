// Aspire injects the API address as services__api__http__0; fall back to the API's own launch profile.
const target = process.env['services__api__https__0'] ?? process.env['services__api__http__0'] ?? 'http://localhost:5080';

export default {
  '/api': { target, secure: false, changeOrigin: true },
  '/hubs': { target, secure: false, changeOrigin: true, ws: true },
};
