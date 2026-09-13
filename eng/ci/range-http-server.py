#!/usr/bin/env python3
"""A static file server that answers Range requests.

The two buffering tutorials — BasicTutorial12 and PlaybackTutorial04 — have to
be served over http rather than from a file, because a file:// URI never
buffers and never downloads. souphttpsrc advertises itself as seekable and
oggdemux uses that: it seeks to the end of the stream to read the duration.
Python's stock `http.server` ignores Range entirely and answers 200 with the
whole body, so that seek comes back as a full re-read, the source reports that
the server does not support seeking, and the pipeline posts an error at exactly
the point where buffering reaches 100%.

This is the smallest thing that fixes it: the standard handler plus a single
byte range. Standard library only, so a CI leg needs nothing installed.

Usage: range-http-server.py [port] [--bind ADDRESS]
"""

import argparse
import os
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer


class RangeHandler(SimpleHTTPRequestHandler):
    """Serves the working directory, honouring one `Range: bytes=a-b`."""

    def send_head(self):
        self.remaining = None
        path = self.translate_path(self.path)

        if os.path.isdir(path):
            return super().send_head()

        try:
            body = open(path, "rb")
        except OSError:
            self.send_error(404, "File not found")
            return None

        size = os.fstat(body.fileno()).st_size
        asked = self.headers.get("Range")
        span = self.parse_range(asked, size) if asked else None

        if asked and span is None:
            body.close()
            self.send_response(416, "Requested Range Not Satisfiable")
            self.send_header("Content-Range", "bytes */%d" % size)
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return None

        start, end = span if span else (0, size - 1)
        self.remaining = size if span is None else end - start + 1
        body.seek(start)

        self.send_response(206 if span else 200)
        self.send_header("Content-Type", self.guess_type(path))
        self.send_header("Accept-Ranges", "bytes")
        if span:
            self.send_header("Content-Range", "bytes %d-%d/%d" % (start, end, size))
        self.send_header("Content-Length", str(self.remaining))
        self.send_header("Last-Modified", self.date_time_string(os.stat(path).st_mtime))
        self.end_headers()
        return body

    def copyfile(self, source, outputfile):
        """Writes exactly the bytes that were promised, and no more."""
        if self.remaining is None:
            super().copyfile(source, outputfile)
            return

        remaining = self.remaining
        while remaining > 0:
            chunk = source.read(min(64 * 1024, remaining))
            if not chunk:
                break
            outputfile.write(chunk)
            remaining -= len(chunk)

    @staticmethod
    def parse_range(value, size):
        """Reads one byte range, or None when it cannot be satisfied."""
        if not value.startswith("bytes=") or "," in value:
            return None

        first, dash, last = value[len("bytes="):].strip().partition("-")

        if not dash:
            return None

        try:
            if first:
                start = int(first)
                end = int(last) if last else size - 1
            else:
                # bytes=-N asks for the last N bytes.
                start, end = max(size - int(last), 0), size - 1
        except ValueError:
            return None

        end = min(end, size - 1)
        return None if size == 0 or start < 0 or start > end else (start, end)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("port", nargs="?", type=int, default=8765)
    parser.add_argument("--bind", default="127.0.0.1")
    arguments = parser.parse_args()

    server = ThreadingHTTPServer((arguments.bind, arguments.port), RangeHandler)
    print(
        "Serving %s on %s:%d with byte ranges"
        % (os.getcwd(), arguments.bind, arguments.port),
        file=sys.stderr,
    )
    server.serve_forever()


if __name__ == "__main__":
    main()
