import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { HubConnectionState } from '@microsoft/signalr';

import { SignalRService } from './services/signalr.service';

interface NavLink {
  path: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'bc-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  private readonly signalr = inject(SignalRService);

  readonly HubConnectionState = HubConnectionState;
  readonly state$ = this.signalr.state$;

  readonly navLinks: NavLink[] = [
    { path: '/', label: 'Dashboard', icon: 'pi-th-large' },
    { path: '/traffic', label: 'Traffic Inspector', icon: 'pi-list' },
    { path: '/scanner', label: 'Scanner', icon: 'pi-search' },
    { path: '/workbench', label: 'Payload Workbench', icon: 'pi-code' },
    { path: '/browser', label: 'Browser Engine', icon: 'pi-desktop' },
  ];

  ngOnInit(): void {
    void this.signalr.start();
  }

  stateLabel(state: HubConnectionState | null): string {
    switch (state) {
      case HubConnectionState.Connected:
        return 'Connected';
      case HubConnectionState.Connecting:
        return 'Connecting';
      case HubConnectionState.Reconnecting:
        return 'Reconnecting';
      case HubConnectionState.Disconnected:
        return 'Disconnected';
      case HubConnectionState.Disconnecting:
        return 'Disconnecting';
      default:
        return 'Unknown';
    }
  }

  stateClass(state: HubConnectionState | null): string {
    return state === HubConnectionState.Connected
      ? 'bc-state-ok'
      : state === HubConnectionState.Reconnecting || state === HubConnectionState.Connecting
        ? 'bc-state-warn'
        : 'bc-state-err';
  }
}
