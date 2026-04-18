import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import { BlazorMessage, SessionInfo } from '../models/blazor-message.model';

/**
 * Singleton wrapper around the /hubs/traffic SignalR connection.
 * - Auto-reconnect with exponential backoff.
 * - Typed observables for each server→client event.
 *
 * Subscribe from components; the connection is started lazily once and
 * kept alive for the lifetime of the app.
 */
@Injectable({ providedIn: 'root' })
export class SignalRService implements OnDestroy {
  private hub?: signalR.HubConnection;
  private started = false;

  private readonly connectionState$ = new BehaviorSubject<signalR.HubConnectionState>(
    signalR.HubConnectionState.Disconnected,
  );
  private readonly messages = new Subject<BlazorMessage>();
  private readonly sessionsOpened = new Subject<SessionInfo>();
  private readonly sessionsClosed = new Subject<{ sessionId: string }>();
  private readonly trafficCleared = new Subject<void>();
  private readonly stageChanged = new Subject<void>();

  readonly state$ = this.connectionState$.asObservable();
  readonly messages$ = this.messages.asObservable();
  readonly sessionOpened$ = this.sessionsOpened.asObservable();
  readonly sessionClosed$ = this.sessionsClosed.asObservable();
  readonly trafficCleared$ = this.trafficCleared.asObservable();
  readonly stageChanged$ = this.stageChanged.asObservable();

  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;

    this.hub = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/traffic')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.hub.on('MessageIntercepted', (msg: BlazorMessage) => this.messages.next(msg));
    this.hub.on('SessionOpened', (s: SessionInfo) => this.sessionsOpened.next(s));
    this.hub.on('SessionClosed', (s: { sessionId: string }) => this.sessionsClosed.next(s));
    this.hub.on('TrafficCleared', () => this.trafficCleared.next());
    this.hub.on('StageChanged', () => this.stageChanged.next());

    this.hub.onreconnecting(() =>
      this.connectionState$.next(signalR.HubConnectionState.Reconnecting),
    );
    this.hub.onreconnected(() =>
      this.connectionState$.next(signalR.HubConnectionState.Connected),
    );
    this.hub.onclose(() =>
      this.connectionState$.next(signalR.HubConnectionState.Disconnected),
    );

    try {
      await this.hub.start();
      this.connectionState$.next(signalR.HubConnectionState.Connected);
    } catch (err) {
      console.warn('[SignalR] initial connect failed, will retry', err);
      // Schedule a retry — the built-in reconnect only engages after a
      // first successful connect, so we roll our own here.
      setTimeout(() => {
        this.started = false;
        void this.start();
      }, 5000);
    }
  }

  ngOnDestroy(): void {
    void this.hub?.stop();
  }
}
