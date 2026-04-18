/**
 * Wire-format message captured by the MITM proxy.
 * Mirrors BlazeCannon.Core.Models.BlazorMessage (camelCase-serialized).
 */
export type MessageDirection = 'ClientToServer' | 'ServerToClient';

/**
 * Server serializes the BlazorMessageType enum as a string.
 * Kept as a plain string union so new types added on the backend
 * don't break the client — unknown values simply render as-is.
 */
export type BlazorMessageType =
  | 'Handshake'
  | 'Invocation'
  | 'StreamItem'
  | 'Completion'
  | 'StreamInvocation'
  | 'CancelInvocation'
  | 'Ping'
  | 'Close'
  | 'DispatchBrowserEvent'
  | 'RenderBatch'
  | 'OnRenderCompleted'
  | 'OnLocationChanged'
  | 'BeginInvokeDotNet'
  | 'EndInvokeJS'
  | 'ReceiveByteArray'
  | 'AttachComponent'
  | 'Unknown'
  | string;

export const BLAZOR_MESSAGE_TYPES: BlazorMessageType[] = [
  'Handshake',
  'Invocation',
  'StreamItem',
  'Completion',
  'StreamInvocation',
  'CancelInvocation',
  'Ping',
  'Close',
  'DispatchBrowserEvent',
  'RenderBatch',
  'OnRenderCompleted',
  'OnLocationChanged',
  'BeginInvokeDotNet',
  'EndInvokeJS',
  'ReceiveByteArray',
  'AttachComponent',
  'Unknown',
];

export interface BlazorMessage {
  timestamp: string;
  direction: MessageDirection;
  messageType: BlazorMessageType;
  hubMethod?: string;
  rawPayload: string;
  decodedArguments?: unknown[];
  sequenceId?: number;
  invocationId?: string;
  sessionId?: string;
  /** Base64-encoded raw binary payload. Present for MessagePack / binary frames. */
  rawBinaryPayload?: string;
  host?: string;
  hubPath?: string;
  transport?: string;
}

export interface ProxyStatus {
  isRunning: boolean;
  uiPort: number;
  proxyPort: number;
  activeSessionCount: number;
  capturedCount: number;
}

export interface SessionInfo {
  sessionId: string;
  host?: string;
  hubPath?: string;
  transport?: string;
}

export interface ReplaySendResult {
  sentAt?: string;
  error?: string;
}

/**
 * Request body for POST /api/replay/encode-and-send.
 * Server encodes an Invocation from these fields and replays it on the live session.
 */
export interface EncodeAndSendRequest {
  messageType: 'Invocation';
  hubMethod: string;
  invocationId?: string;
  /** Must be a JSON array. Values may be primitives, arrays, or objects. */
  arguments: unknown[];
  useMessagePack: boolean;
  sessionId?: string;
}

export interface EncodeAndSendResponse {
  sentAt: string;
  byteLength: number;
  /** Populated when MessagePack encoding was used. */
  wireFormatBase64?: string | null;
  /** Populated when JSON-text encoding was used. */
  rawText?: string | null;
}

export interface TrafficFilter {
  direction?: MessageDirection | '';
  type?: BlazorMessageType | '';
  sessionId?: string;
  search?: string;
  limit?: number;
}
