import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { DataSetSummary } from '../../core/models/dataset.model';

@Component({
  selector: 'app-dataset-switcher',
  standalone: true,
  imports: [CommonModule, FormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule],
  templateUrl: './dataset-switcher.component.html',
  styleUrls: ['./dataset-switcher.component.scss']
})
export class DatasetSwitcherComponent {
  @Input() datasets: DataSetSummary[] = [];
  @Input() selectedDatasetId: string | null = null;
  @Output() selectedDatasetIdChange = new EventEmitter<string>();

  search = '';

  get filteredDatasets(): DataSetSummary[] {
    const term = this.search.trim().toLowerCase();
    if (!term) {
      return this.datasets;
    }

    return this.datasets.filter(item => item.originalFileName.toLowerCase().includes(term));
  }

  onSelectionChange(datasetId: string): void {
    if (datasetId) {
      this.selectedDatasetIdChange.emit(datasetId);
    }
  }
}
