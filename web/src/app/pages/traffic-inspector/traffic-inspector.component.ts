import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  HostListener,
  OnInit,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { ButtonModule } from 'primeng/button';
import { DropdownModule } from 'primeng/dropdown';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';

import {
  BLAZOR_MESSAGE_TYPES,
  BlazorMessage,
  BlazorMessageType,
  MessageDirection,
  TrafficFilter,
} from '../../models/blazor-message.model';
import { ReplayApiService } from '../../services/replay-api.service';
import { SignalRService } from '../../services/signalr.service';
import { TrafficApiService } from '../../services/traffic-api.service';

interface ColumnDef {
  key: string;
  label: string;
}

const MAX_MESSAGES = 500;
const COLUMN_ORDER_KEY = 'blazecannon.traffic.columnOrder';

const DEFAULT_COLUMNS: ColumnDef[] = [
  { key: 'time', label: 'Time' },
  { key: 'dir', label: 'Dir' },
  { key: 'type', label: 'Type' },
  { key: 'method', label: 'Method' },
  { key: 'host', label: 'Host' },
  { key: 'hub', label: 'Blazor Hub' },
  { key: 'transport', label: 'Transport' },
  { key: 'preview', label: 'Preview' },
];

@Component({
  selector: 'bc-traffic-inspector',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    DropdownModule,
    InputTextModule,
    TableModule,
    TagModule,
    TooltipModule,
    DialogModule,
    MessageModule,
  ],
  templateUrl: './traffic-inspector.component.html',
  styleUrl: './traffic-inspector.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrafficInspectorComponent implements OnInit {
  private readonly trafficApi = inject(TrafficApiService);
  private readonly replayApi = inject(ReplayApiService);
  private readonly signalr = inject(SignalRService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  messages: BlazorMessage[] = [];
  filtered: BlazorMessage[] = [];
  selected: BlazorMessage | null = null;

  // filter state
  directionFilter: MessageDirection | '' = '';
  typeFilter: BlazorMessageType | '' = '';
  search = '';

  readonly directionOptions = [
    { label: 'All directions', value: '' },
    { label: 'Client → Server', value: 'ClientToServer' },
    { label: 'Server → Client', value: 'ServerToClient' },
  ];

  readonly typeOptions = [
    { label: 'All types', value: '' },
    ...BLAZOR_MESSAGE_TYPES.map((t) => ({ label: t, value: t })),
  ];

  columns: ColumnDef[] = [...DEFAULT_COLUMNS];

  stageFeedback: { ok: boolean; text: string } | null = null;
  loadError: string | null = null;

  ngOnInit(): void {
    this.loadColumnOrder();
    this.loadInitial();

    this.signalr.messages$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((msg) => this.onNewMessage(msg));

    this.signalr.trafficCleared$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.messages = [];
        this.applyFilters();
        this.selected = null;
        this.cdr.markForCheck();
      });
  }

  // ---- Load / filter -------------------------------------------------------

  private loadInitial(): void {
    this.trafficApi.list({ limit: MAX_MESSAGES }).subscribe({
      next: (msgs) => {
        this.messages = msgs.slice(-MAX_MESSAGES);
        this.applyFilters();
        this.cdr.markForCheck();
      },
      error: (err) => {
        this.loadError = err?.message ?? 'Failed to load traffic';
        this.cdr.markForCheck();
      },
    });
  }

  private onNewMessage(msg: BlazorMessage): void {
    this.messages = [...this.messages, msg].slice(-MAX_MESSAGES);
    this.applyFilters();
    this.cdr.markForCheck();
  }

  applyFilters(): void {
    const q = this.search.trim().toLowerCase();
    this.filtered = this.messages.filter((m) => {
      if (this.directionFilter && m.direction !== this.directionFilter) return false;
      if (this.typeFilter && m.messageType !== this.typeFilter) return false;
      if (q) {
        const hay = [m.rawPayload, m.hubMethod, m.host, m.hubPath]
          .filter(Boolean)
          .join('\n')
          .toLowerCase();
        if (!hay.includes(q)) return false;
      }
      return true;
    });
    this.cdr.markForCheck();
  }

  onFilterChange(): void {
    this.applyFilters();
  }

  // ---- Row selection -------------------------------------------------------

  onRowSelect(msg: BlazorMessage): void {
    this.selected = this.selected === msg ? null : msg;
    this.cdr.markForCheck();
  }

  closeDetail(): void {
    this.selected = null;
    this.cdr.markForCheck();
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(ev: KeyboardEvent): void {
    if (ev.key !== 'ArrowUp' && ev.key !== 'ArrowDown') return;

    // Don't intercept while the user is typing in an input / textarea / select
    const target = ev.target as HTMLElement | null;
    const tag = target?.tagName?.toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select') return;
    if (target?.isContentEditable) return;
    if (this.filtered.length === 0) return;

    ev.preventDefault();

    const curIdx = this.selected ? this.filtered.indexOf(this.selected) : -1;
    let nextIdx: number;
    if (ev.key === 'ArrowDown') {
      nextIdx = curIdx < 0 ? 0 : Math.min(curIdx + 1, this.filtered.length - 1);
    } else {
      nextIdx =
        curIdx < 0 ? this.filtered.length - 1 : Math.max(curIdx - 1, 0);
    }
    if (nextIdx === curIdx) return;

    this.selected = this.filtered[nextIdx];
    this.cdr.markForCheck();

    // Scroll the newly-selected row into view after render.
    queueMicrotask(() => {
      document
        .querySelector('.bc-traffic-table tr.bc-selected')
        ?.scrollIntoView({ block: 'nearest' });
    });
  }

  // ---- Toolbar actions -----------------------------------------------------

  clear(): void {
    this.trafficApi.clear().subscribe({
      next: () => {
        this.messages = [];
        this.applyFilters();
        this.selected = null;
        this.cdr.markForCheck();
      },
    });
  }

  exportJson(): void {
    this.downloadExport('json');
  }

  exportCsv(): void {
    this.downloadExport('csv');
  }

  private downloadExport(format: 'json' | 'csv'): void {
    const filter: TrafficFilter = {
      direction: this.directionFilter || undefined,
      type: this.typeFilter || undefined,
      search: this.search || undefined,
    };
    this.trafficApi.exportBlob(format, filter).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `blazecannon-traffic-${timestampSlug()}.${format}`;
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
      },
    });
  }

  stageSelected(openReplay: boolean): void {
    if (!this.selected) return;
    const msg = this.selected;
    this.replayApi.stage(msg).subscribe({
      next: () => {
        this.stageFeedback = { ok: true, text: 'Staged for replay' };
        this.cdr.markForCheck();
        if (openReplay) this.router.navigate(['/replay']);
      },
      error: (err) => {
        this.stageFeedback = {
          ok: false,
          text: err?.message ?? 'Failed to stage message',
        };
        this.cdr.markForCheck();
      },
    });
  }

  // ---- Column reordering ---------------------------------------------------

  /**
   * Handler invoked after PrimeNG p-table drag-drop reorder.
   * The mutation happens on our array, so we just persist.
   */
  onColReorder(): void {
    this.persistColumnOrder();
  }

  resetColumns(): void {
    this.columns = [...DEFAULT_COLUMNS];
    this.persistColumnOrder();
    this.cdr.markForCheck();
  }

  private persistColumnOrder(): void {
    try {
      const keys = this.columns.map((c) => c.key).join(',');
      localStorage.setItem(COLUMN_ORDER_KEY, keys);
    } catch {
      /* storage unavailable — ignore */
    }
  }

  private loadColumnOrder(): void {
    try {
      const stored = localStorage.getItem(COLUMN_ORDER_KEY);
      if (!stored) return;
      const parsedKeys = stored.split(',').filter(Boolean);
      const byKey = new Map(DEFAULT_COLUMNS.map((c) => [c.key, c]));
      if (
        parsedKeys.length === DEFAULT_COLUMNS.length &&
        parsedKeys.every((k) => byKey.has(k))
      ) {
        this.columns = parsedKeys.map((k) => byKey.get(k)!);
      }
    } catch {
      /* ignore */
    }
  }

  // ---- Render helpers ------------------------------------------------------

  formatTime(ts: string): string {
    const d = new Date(ts);
    const hh = pad(d.getHours());
    const mm = pad(d.getMinutes());
    const ss = pad(d.getSeconds());
    const ms = d.getMilliseconds().toString().padStart(3, '0');
    return `${hh}:${mm}:${ss}.${ms}`;
  }

  formatArgs(args: unknown): string {
    try {
      return JSON.stringify(args, null, 2);
    } catch {
      return String(args);
    }
  }

  truncate(s: string | undefined, max = 80): string {
    if (!s) return '';
    return s.length > max ? s.slice(0, max) + '…' : s;
  }

  dirArrow(dir: MessageDirection): string {
    return dir === 'ClientToServer' ? '→' : '←';
  }
}

function pad(n: number): string {
  return n.toString().padStart(2, '0');
}

function timestampSlug(): string {
  const d = new Date();
  return (
    d.getFullYear().toString() +
    pad(d.getMonth() + 1) +
    pad(d.getDate()) +
    '-' +
    pad(d.getHours()) +
    pad(d.getMinutes()) +
    pad(d.getSeconds())
  );
}
