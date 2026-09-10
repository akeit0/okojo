import { TextDecoder } from 'node:util';

/** The wire envelope is intentionally independent of the VS Code extension API. */
export interface ProtocolMessage {
  seq: number;
  type: string;
  [key: string]: any;
}
export interface DapRequest extends ProtocolMessage {
  type: 'request';
  command: string;
  arguments?: Record<string, any>;
}
export interface Disposable { dispose(): void; }

export function encodeMessage(message: ProtocolMessage): Buffer {
  const body = Buffer.from(JSON.stringify(message), 'utf8');
  return Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, 'ascii'), body]);
}

/** Handles arbitrary byte fragmentation, multiple frames and UTF-8 byte lengths. */
export class DapDecoder {
  private buffer: Buffer = Buffer.alloc(0);
  private contentLength: number | undefined;
  private readonly utf8 = new TextDecoder('utf-8', { fatal: true });
  public constructor(private readonly maxMessageBytes = 8 * 1024 * 1024) {}

  public push(chunk: Buffer): ProtocolMessage[] {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    const messages: ProtocolMessage[] = [];
    for (;;) {
      if (this.contentLength === undefined) {
        const end = this.buffer.indexOf('\r\n\r\n');
        if (end < 0) {
          if (this.buffer.length > 8192) throw new Error('DAP header is too large.');
          break;
        }
        if (end > 8192) throw new Error('DAP header is too large.');
        const bytes = this.buffer.subarray(0, end);
        if (bytes.some(byte => byte > 127)) throw new Error('DAP headers must be ASCII.');
        const lengths = bytes.toString('ascii').split('\r\n')
          .filter(line => /^content-length\s*:/i.test(line));
        if (lengths.length !== 1 || !/^content-length:\s*\d+\s*$/i.test(lengths[0])) {
          throw new Error('Expected exactly one valid Content-Length header.');
        }
        const length = Number(lengths[0].slice(lengths[0].indexOf(':') + 1).trim());
        if (!Number.isSafeInteger(length) || length <= 0 || length > this.maxMessageBytes) {
          throw new Error('Invalid or oversized DAP message.');
        }
        this.contentLength = length;
        this.buffer = this.buffer.subarray(end + 4);
      }
      if (this.buffer.length < this.contentLength) break;
      const value = JSON.parse(this.utf8.decode(this.buffer.subarray(0, this.contentLength)));
      this.buffer = this.buffer.subarray(this.contentLength);
      this.contentLength = undefined;
      if (!value || typeof value !== 'object' || Array.isArray(value)
        || !Number.isInteger(value.seq) || value.seq < 0 || typeof value.type !== 'string') {
        throw new Error('Invalid DAP message envelope.');
      }
      messages.push(value);
    }
    return messages;
  }

  public finish(): void {
    if (this.contentLength !== undefined || this.buffer.length !== 0) {
      throw new Error('Truncated DAP frame at EOF.');
    }
  }
}
