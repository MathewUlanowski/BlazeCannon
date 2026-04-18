import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { CardModule } from 'primeng/card';

@Component({
  selector: 'bc-placeholder',
  standalone: true,
  imports: [CommonModule, CardModule],
  template: `
    <div class="bc-placeholder">
      <p-card>
        <ng-template pTemplate="title">{{ title }}</ng-template>
        <ng-template pTemplate="subtitle">Coming soon</ng-template>
        <p class="bc-placeholder-body">
          This module is not yet wired up. Check back in a future release.
        </p>
      </p-card>
    </div>
  `,
  styles: [
    `
      .bc-placeholder {
        max-width: 560px;
        margin: 40px auto;
      }
      .bc-placeholder-body {
        color: var(--bc-text-dim);
        font-size: 14px;
      }
    `,
  ],
})
export class PlaceholderComponent {
  private readonly route = inject(ActivatedRoute);

  title: string =
    (this.route.snapshot.data['title'] as string | undefined) ?? 'Coming soon';
}
