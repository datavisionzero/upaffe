// Disposable SMTP receiver for the composed system test. It has no production role.
import net from "node:net";
import http from "node:http";

const messages = [];
let mode = "accept";
let submissions = 0;

net.createServer((socket) => {
  socket.setEncoding("utf8");
  socket.write("220 smtp-fixture ESMTP\r\n");
  let buffer = "";
  let data = false;
  let lines = [];
  let sender = "";
  let recipient = "";
  socket.on("data", (chunk) => {
    buffer += chunk;
    for (;;) {
      const boundary = buffer.indexOf("\n");
      if (boundary < 0) break;
      const line = buffer.slice(0, boundary).replace(/\r$/, "");
      buffer = buffer.slice(boundary + 1);
      if (data) {
        if (line === ".") {
          submissions++;
          messages.push({ sender, recipient, content: lines.join("\n"), accepted_at: new Date().toISOString() });
          socket.write("250 2.0.0 queued\r\n");
          data = false;
          lines = [];
        } else lines.push(line.startsWith("..") ? line.slice(1) : line);
        continue;
      }
      const command = line.slice(0, 4).toUpperCase();
      if (command === "EHLO") socket.write("250-smtp-fixture\r\n250 8BITMIME\r\n");
      else if (command === "HELO") socket.write("250 smtp-fixture\r\n");
      else if (command === "MAIL") { sender = line.slice(10); socket.write("250 2.1.0 ok\r\n"); }
      else if (command === "RCPT") {
        recipient = line.slice(8);
        socket.write(mode === "temporary" ? "451 4.3.0 temporarily unavailable\r\n" : mode === "reject" ? "550 5.1.1 rejected\r\n" : "250 2.1.5 ok\r\n");
      }
      else if (command === "DATA") { data = true; lines = []; socket.write("354 end with dot\r\n"); }
      else if (command === "RSET") { sender = ""; recipient = ""; lines = []; data = false; socket.write("250 2.0.0 reset\r\n"); }
      else if (command === "NOOP") socket.write("250 2.0.0 ok\r\n");
      else if (command === "QUIT") { socket.write("221 2.0.0 bye\r\n"); socket.end(); }
      else socket.write("502 5.5.1 unsupported\r\n");
    }
  });
}).listen(2525, "0.0.0.0");

http.createServer((request, response) => {
  response.setHeader("Content-Type", "application/json");
  if (request.method === "GET" && request.url === "/messages") {
    response.end(JSON.stringify({ mode, submissions, messages }));
  } else if (request.method === "POST" && request.url?.startsWith("/mode/")) {
    const selected = request.url.slice("/mode/".length);
    if (!["accept", "temporary", "reject"].includes(selected)) { response.writeHead(400); response.end("{}"); return; }
    mode = selected;
    response.end(JSON.stringify({ mode }));
  } else if (request.method === "DELETE" && request.url === "/messages") {
    messages.length = 0;
    submissions = 0;
    response.end("{}");
  } else { response.writeHead(404); response.end("{}"); }
}).listen(8025, "0.0.0.0");
