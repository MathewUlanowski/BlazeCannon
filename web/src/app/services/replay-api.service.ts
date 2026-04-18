import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BlazorMessage,
  EncodeAndSendRequest,
  EncodeAndSendResponse,
  ReplaySendResult,
} from '../models/blazor-message.model';

@Injectable({ providedIn: 'root' })
export class ReplayApiService {
  constructor(private http: HttpClient) {}

  listStaged(): Observable<BlazorMessage[]> {
    return this.http.get<BlazorMessage[]>('/api/replay/staged');
  }

  stage(message: BlazorMessage): Observable<void> {
    return this.http.post<void>('/api/replay/stage', message);
  }

  removeStaged(index: number): Observable<void> {
    return this.http.delete<void>(`/api/replay/staged/${index}`);
  }

  clearStaged(): Observable<void> {
    return this.http.delete<void>('/api/replay/staged');
  }

  send(message: BlazorMessage): Observable<ReplaySendResult> {
    return this.http.post<ReplaySendResult>('/api/replay/send', message);
  }

  encodeAndSend(
    body: EncodeAndSendRequest,
  ): Observable<EncodeAndSendResponse> {
    return this.http.post<EncodeAndSendResponse>(
      '/api/replay/encode-and-send',
      body,
    );
  }
}
