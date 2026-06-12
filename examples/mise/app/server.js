const http = require('http');

const port = process.env.PORT ?? 3000;
const greeting = process.env.GREETING ?? 'GREETING was not set';

const server = http.createServer((req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end(`${greeting} (node ${process.version})`);
});

server.listen(port, () => {
  console.log(`Listening on port ${port} with node ${process.version}`);
});
