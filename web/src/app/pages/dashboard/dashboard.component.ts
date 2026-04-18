import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { CardModule } from 'primeng/card';
import { TagModule } from 'primeng/tag';
import { ButtonModule } from 'primeng/button';
import { MessageModule } from 'primeng/message';

import { ProxyApiService } from '../../services/proxy-api.service';
import { SignalRService } from '../../services/signalr.service';
import { ProxyStatus } from '../../models/blazor-message.model';

@Component({
  selector: 'bc-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, CardModule, TagModule, ButtonModule, MessageModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent implements OnInit {
  private readonly proxyApi = inject(ProxyApiService);
  private readonly signalr = inject(SignalRService);
  private readonly destroyRef = inject(DestroyRef);

  status: ProxyStatus | null = null;
  error: string | null = null;
  loading = true;

  ngOnInit(): void {
    this.refresh();

    // Live counters — don't re-hit the API, just nudge the cached values.
    this.signalr.messages$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.status) this.status = { ...this.status, capturedCount: this.status.capturedCount + 1 };
      });

    this.signalr.sessionOpened$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.status) this.status = { ...this.status, activeSessionCount: this.status.activeSessionCount + 1 };
      });

    this.signalr.sessionClosed$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.status && this.status.activeSessionCount > 0)
          this.status = { ...this.status, activeSessionCount: this.status.activeSessionCount - 1 };
      });

    this.signalr.trafficCleared$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.status) this.status = { ...this.status, capturedCount: 0 };
      });
  }

  refresh(): void {
    this.loading = true;
    this.error = null;
    this.proxyApi.getStatus().subscribe({
      next: (s) => {
        this.status = s;
        this.loading = false;
      },
      error: (err) => {
        this.error = err?.message ?? 'Failed to load proxy status';
        this.loading = false;
      },
    });
  }
}
