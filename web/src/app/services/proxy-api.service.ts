import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ProxyStatus } from '../models/blazor-message.model';

@Injectable({ providedIn: 'root' })
export class ProxyApiService {
  constructor(private http: HttpClient) {}

  getStatus(): Observable<ProxyStatus> {
    return this.http.get<ProxyStatus>('/api/proxy/status');
  }

  getTarget(): Observable<unknown> {
    return this.http.get<unknown>('/api/target');
  }

  setTarget(config: unknown): Observable<unknown> {
    return this.http.put<unknown>('/api/target', config);
  }
}
