import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-workspace-placeholder-page',
  standalone: true,
  imports: [CommonModule, MatCardModule],
  templateUrl: './workspace-placeholder-page.component.html',
  styleUrls: ['./workspace-placeholder-page.component.scss']
})
export class WorkspacePlaceholderPageComponent {
  private readonly route = inject(ActivatedRoute);

  get title(): string {
    return this.route.snapshot.data['title'] || 'Workspace';
  }
}
