const http = require('http');
const leftPad = require('left-pad');

const port = process.env.PORT ?? 3000;
const greeting = process.env.GREETING ?? 'GREETING was not set';

const server = http.createServer((req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end(`${leftPad('js', 4)}: ${greeting} (node ${process.version})`);
});

server.listen(port, () => {
  console.log(`jsapp listening on port ${port} with node ${process.version}`);
});
