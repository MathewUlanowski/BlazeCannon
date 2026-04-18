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
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { ButtonModule } from 'primeng/button';
import { DropdownModule } from 'primeng/dropdown';
import { InputTextModule } from 'primeng/inputtext';
import { InputTextareaModule } from 'primeng/inputtextarea';
import { InputSwitchModule } from 'primeng/inputswitch';
import { TableModule } from 'primeng/table';
import { TabViewModule } from 'primeng/tabview';
import { TagModule } from 'primeng/tag';
import { TooltipModule } from 'primeng/tooltip';
import { DialogModule } from 'primeng/dialog';
import { MessageModule } from 'primeng/message';

import {
  BLAZOR_MESSAGE_TYPES,
  BlazorMessage,
  BlazorMessageType,
  EncodeAndSendRequest,
  MessageDirection,
  ProxyStatus,
  TrafficFilter,
} from '../../models/blazor-message.model';
import { ProxyApiService } from '../../services/proxy-api.service';
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
    InputTextareaModule,
    InputSwitchModule,
    TableModule,
    TabViewModule,
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
  private readonly proxyApi = inject(ProxyApiService);
  private readonly signalr = inject(SignalRService);
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

  loadError: string | null = null;

  // ---- Editor state (merged from former Replay page) -----------------------

  /** Edited rawPayload buffer bound to the Raw-tab textarea. */
  editedPayload = '';

  status: ProxyStatus | null = null;

  sendResult: { ok: boolean; at?: string; error?: string; info?: string } | null = null;
  sending = false;

  decodedHubMethod = '';
  decodedInvocationId = '';
  decodedArgsJson = '[]';
  decodedUseMessagePack = false;
  decodedJsonError: string | null = null;
  decodedSendResult: { ok: boolean; at?: string; error?: string; info?: string } | null = null;
  decodedSending = false;

  ngOnInit(): void {
    this.loadColumnOrder();
    this.loadInitial();
    this.refreshStatus();

    this.signalr.messages$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((msg) => this.onNewMessage(msg));

    this.signalr.trafficCleared$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.messages = [];
        this.applyFilters();
        this.clearSelection();
        this.cdr.markForCheck();
      });

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

  private refreshStatus(): void {
    this.proxyApi.getStatus().subscribe({
      next: (s) => {
        this.status = s;
        this.cdr.markForCheck();
      },
      // silent; status is nice-to-have here
      error: () => {},
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
    if (this.selected === msg) {
      this.clearSelection();
    } else {
      this.selected = msg;
      this.primeEditorState();
    }
    this.cdr.markForCheck();
  }

  closeDetail(): void {
    this.clearSelection();
    this.cdr.markForCheck();
  }

  private clearSelection(): void {
    this.selected = null;
    this.editedPayload = '';
    this.sendResult = null;
    this.decodedSendResult = null;
    this.decodedJsonError = null;
  }

  private primeEditorState(): void {
    const s = this.selected;
    this.editedPayload = s?.rawPayload ?? '';
    this.decodedHubMethod = s?.hubMethod ?? '';
    this.decodedInvocationId = s?.invocationId ?? '';
    const args = s?.decodedArguments ?? [];
    try {
      this.decodedArgsJson = JSON.stringify(args, null, 2);
    } catch {
      this.decodedArgsJson = '[]';
    }
    this.decodedUseMessagePack = !!s?.rawBinaryPayload;
    this.decodedJsonError = null;
    this.sendResult = null;
    this.decodedSendResult = null;
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(ev: KeyboardEvent): void {
    if (ev.key === 'Escape' && this.selected) {
      const target = ev.target as HTMLElement | null;
      const tag = target?.tagName?.toLowerCase();
      // Allow Escape to blur inputs first rather than closing the panel.
      if (tag === 'input' || tag === 'textarea' || tag === 'select') return;
      if (target?.isContentEditable) return;
      ev.preventDefault();
      this.clearSelection();
      this.cdr.markForCheck();
      return;
    }

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
    this.primeEditorState();
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
        this.clearSelection();
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

  // ---- Send (Raw tab) ------------------------------------------------------

  get isBinary(): boolean {
    return !!this.selected?.rawBinaryPayload;
  }

  get binaryByteCount(): number {
    if (!this.selected?.rawBinaryPayload) return 0;
    // base64 → byte count estimate
    const b64 = this.selected.rawBinaryPayload;
    const padding = b64.endsWith('==') ? 2 : b64.endsWith('=') ? 1 : 0;
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

  // ---- Send (Decoded tab) --------------------------------------------------

  /**
   * True when the selected message is an Invocation and the Decoded tab is usable.
   * Any other message type is out of scope for this release.
   */
  get decodedSupported(): boolean {
    return this.selected?.messageType === 'Invocation';
  }

  get decodedSupportedTooltip(): string {
    return this.decodedSupported
      ? ''
      : 'Decoded editing supports Invocation messages only in this release';
  }

  onDecodedArgsChange(value: string): void {
    this.decodedArgsJson = value;
    // Live parse so the user sees errors before hitting send.
    try {
      const parsed = JSON.parse(value);
      if (!Array.isArray(parsed)) {
        this.decodedJsonError = 'Arguments must be a JSON array.';
      } else {
        this.decodedJsonError = null;
      }
    } catch (e: unknown) {
      this.decodedJsonError = (e as Error)?.message ?? 'Invalid JSON';
    }
  }

  get canSendDecoded(): boolean {
    return (
      this.decodedSupported &&
      !this.decodedSending &&
      !!this.status &&
      this.status.activeSessionCount > 0 &&
      !this.decodedJsonError &&
      !!this.decodedHubMethod.trim()
    );
  }

  sendDecoded(): void {
    if (!this.selected || !this.canSendDecoded) return;

    let parsed: unknown;
    try {
      parsed = JSON.parse(this.decodedArgsJson);
    } catch (e: unknown) {
      this.decodedJsonError = (e as Error)?.message ?? 'Invalid JSON';
      return;
    }
    if (!Array.isArray(parsed)) {
      this.decodedJsonError = 'Arguments must be a JSON array.';
      return;
    }

    const body: EncodeAndSendRequest = {
      messageType: 'Invocation',
      hubMethod: this.decodedHubMethod.trim(),
      invocationId: this.decodedInvocationId.trim() || undefined,
      arguments: parsed as unknown[],
      useMessagePack: this.decodedUseMessagePack,
      sessionId: this.selected.sessionId,
    };

    this.decodedSending = true;
    this.decodedSendResult = null;

    this.replayApi.encodeAndSend(body).subscribe({
      next: (r) => {
        this.decodedSending = false;
        const wire = body.useMessagePack ? 'MessagePack' : 'text';
        this.decodedSendResult = {
          ok: true,
          at: r.sentAt,
          info: `Sent ${r.byteLength} bytes via ${wire}`,
        };
        this.cdr.markForCheck();
      },
      error: (err: unknown) => {
        this.decodedSending = false;
        const httpErr = err as HttpErrorResponse;
        const msg =
          httpErr?.error?.error ?? httpErr?.message ?? 'Request failed';
        this.decodedSendResult = { ok: false, error: msg };
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
