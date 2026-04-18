import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { BlazorMessage, TrafficFilter } from '../models/blazor-message.model';

@Injectable({ providedIn: 'root' })
export class TrafficApiService {
  constructor(private http: HttpClient) {}

  list(filter: TrafficFilter = {}): Observable<BlazorMessage[]> {
    return this.http.get<BlazorMessage[]>('/api/traffic', {
      params: this.toParams(filter),
    });
  }

  clear(): Observable<void> {
    return this.http.delete<void>('/api/traffic');
  }

  /**
   * Triggers the browser to download an export.
   * Returns the object URL so the caller can revoke it after click().
   */
  exportBlob(
    format: 'json' | 'csv',
    filter: TrafficFilter = {},
  ): Observable<Blob> {
    const params = this.toParams(filter).set('format', format);
    return this.http.get('/api/traffic/export', {
      params,
      responseType: 'blob',
    });
  }

  private toParams(filter: TrafficFilter): HttpParams {
    let p = new HttpParams();
    if (filter.direction) p = p.set('direction', filter.direction);
    if (filter.type) p = p.set('type', filter.type);
    if (filter.sessionId) p = p.set('sessionId', filter.sessionId);
    if (filter.search) p = p.set('search', filter.search);
    if (filter.limit != null) p = p.set('limit', String(filter.limit));
    return p;
  }
}
