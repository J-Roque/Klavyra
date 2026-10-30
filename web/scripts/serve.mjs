// `npm start`: ng serve on the port Aspire assigns (PORT), defaulting to 4200 when run on its own.
import { spawn } from 'node:child_process';

const port = process.env['PORT'] ?? '4200';
const child = spawn('npx', ['ng', 'serve', '--port', port, '--proxy-config', 'proxy.conf.mjs'], {
  stdio: 'inherit',
  shell: process.platform === 'win32',
});
child.on('exit', (code) => process.exit(code ?? 0));
