import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  OnInit,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { ButtonModule } from 'primeng/button';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { MessageModule } from 'primeng/message';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';

import {
  BlazorMessage,
  ProxyStatus,
} from '../../models/blazor-message.model';
import { ProxyApiService } from '../../services/proxy-api.service';
import { ReplayApiService } from '../../services/replay-api.service';
import { SignalRService } from '../../services/signalr.service';

@Component({
  selector: 'bc-replay',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    ButtonModule,
    InputTextareaModule,
    MessageModule,
    TagModule,
    TooltipModule,
  ],
  templateUrl: './replay.component.html',
  styleUrl: './replay.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReplayComponent implements OnInit {
  private readonly replayApi = inject(ReplayApiService);
  private readonly proxyApi = inject(ProxyApiService);
  private readonly signalr = inject(SignalRService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  staged: BlazorMessage[] = [];
  selectedIndex: number | null = null;
  selected: BlazorMessage | null = null;

  /** Edited rawPayload buffer bound to the textarea. */
  editedPayload = '';

  /** Session-filtered live traffic for the selected staged message. */
  sessionTraffic: BlazorMessage[] = [];

  status: ProxyStatus | null = null;

  sendResult: { ok: boolean; at?: string; error?: string } | null = null;
  sending = false;
  loadError: string | null = null;

  ngOnInit(): void {
    this.refreshStaged();
    this.refreshStatus();

    this.signalr.stageChanged$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.refreshStaged());

    this.signalr.sessionOpened$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.status)
          this.status = {
            ...this.status,
            activeSessionCount: this.status.activeSessionCount + 1,
          };
        this.cdr.markForCheck();
      });

    this.signalr.sessionClosed$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.status && this.status.activeSessionCount > 0)
          this.status = {
            ...this.status,
            activeSessionCount: this.status.activeSessionCount - 1,
          };
        this.cdr.markForCheck();
      });

    this.signalr.messages$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((msg) => {
        if (
          this.selected?.sessionId &&
          msg.sessionId === this.selected.sessionId
        ) {
          this.sessionTraffic = [...this.sessionTraffic, msg].slice(-200);
          this.cdr.markForCheck();
        }
      });
  }

  // ---- Data loading --------------------------------------------------------

  private refreshStaged(): void {
    this.replayApi.listStaged().subscribe({
      next: (items) => {
        this.staged = items;
        // Keep selection stable by sessionId+timestamp if possible.
        if (this.selectedIndex !== null) {
          if (this.selectedIndex >= this.staged.length) {
            this.selectedIndex = this.staged.length
              ? this.staged.length - 1
              : null;
          }
          this.syncSelected();
        }
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.loadError = err?.message ?? 'Failed to load staged queue';
        this.cdr.markForCheck();
      },
    });
  }

  private refreshStatus(): void {
    this.proxyApi.getStatus().subscribe({
      next: (s) => {
        this.status = s;
        this.cdr.markForCheck();
      },
      // silent; status is nice-to-have on this page
      error: () => {},
    });
  }

  // ---- Selection -----------------------------------------------------------

  select(index: number): void {
    this.selectedIndex = index;
    this.syncSelected();
    this.sessionTraffic = [];
    this.sendResult = null;
  }

  private syncSelected(): void {
    this.selected =
      this.selectedIndex !== null ? (this.staged[this.selectedIndex] ?? null) : null;
    this.editedPayload = this.selected?.rawPayload ?? '';
    this.cdr.markForCheck();
  }

  // ---- Staged actions ------------------------------------------------------

  remove(index: number, ev: Event): void {
    ev.stopPropagation();
    this.replayApi.removeStaged(index).subscribe({
      next: () => {
        // stageChanged SignalR event will fire; fall back to immediate refresh.
        this.refreshStaged();
      },
    });
  }

  clearAll(): void {
    this.replayApi.clearStaged().subscribe({
      next: () => {
        this.selectedIndex = null;
        this.selected = null;
        this.refreshStaged();
      },
    });
  }

  // ---- Send ----------------------------------------------------------------

  get isBinary(): boolean {
    return !!this.selected?.rawBinaryPayload;
  }

  get binaryByteCount(): number {
    if (!this.selected?.rawBinaryPayload) return 0;
    // base64 → byte count estimate
    const b64 = this.selected.rawBinaryPayload;
    const padding = (b64.endsWith('==') ? 2 : b64.endsWith('=') ? 1 : 0);
    return Math.floor((b64.length * 3) / 4) - padding;
  }

  get canSend(): boolean {
    return (
      !!this.selected &&
      !this.sending &&
      !!this.status &&
      this.status.activeSessionCount > 0
    );
  }

  send(): void {
    if (!this.selected || !this.canSend) return;
    const payload: BlazorMessage = {
      ...this.selected,
      rawPayload: this.isBinary ? this.selected.rawPayload : this.editedPayload,
    };
    this.sending = true;
    this.sendResult = null;
    this.replayApi.send(payload).subscribe({
      next: (r) => {
        this.sending = false;
        this.sendResult = {
          ok: !r.error,
          at: r.sentAt,
          error: r.error,
        };
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.sending = false;
        this.sendResult = {
          ok: false,
          error: err?.message ?? 'Request failed',
        };
        this.cdr.markForCheck();
      },
    });
  }

  // ---- Helpers -------------------------------------------------------------

  shortSession(sessionId: string | undefined): string {
    if (!sessionId) return '—';
    return sessionId.length > 8 ? sessionId.slice(0, 8) : sessionId;
  }

  formatTime(ts: string): string {
    const d = new Date(ts);
    return (
      pad(d.getHours()) +
      ':' +
      pad(d.getMinutes()) +
      ':' +
      pad(d.getSeconds()) +
      '.' +
      d.getMilliseconds().toString().padStart(3, '0')
    );
  }

  dirArrow(dir: BlazorMessage['direction']): string {
    return dir === 'ClientToServer' ? '→' : '←';
  }

  trackByIndex(index: number): number {
    return index;
  }
}

function pad(n: number): string {
  return n.toString().padStart(2, '0');
}
