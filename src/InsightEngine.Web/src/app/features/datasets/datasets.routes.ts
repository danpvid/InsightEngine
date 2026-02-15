import { Routes } from '@angular/router';
import { DatasetUploadPageComponent } from './pages/dataset-upload-page/dataset-upload-page.component';
import { RecommendationsPageComponent } from './pages/recommendations-page/recommendations-page.component';
import { ChartViewerPageComponent } from './pages/chart-viewer-page/chart-viewer-page.component';

export const DATASETS_ROUTES: Routes = [
  {
    path: 'new',
    component: DatasetUploadPageComponent
  },
  {
    path: ':datasetId/recommendations',
    component: RecommendationsPageComponent
  },
  {
    path: ':datasetId/charts/:recommendationId',
    component: ChartViewerPageComponent
  }
];
