import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MATERIAL_MODULES } from '../../../../shared/material/material.imports';
import { TranslatePipe } from '../../../../core/pipes/translate.pipe';
import { PageHeaderComponent } from '../../../../shared/components/page-header/page-header.component';

@Component({
  selector: 'app-explore-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    TranslatePipe,
    ...MATERIAL_MODULES,
    PageHeaderComponent
  ],
  templateUrl: './explore-page.component.html',
  styleUrls: ['./explore-page.component.scss']
})
export class ExplorePageComponent implements OnInit {
  datasetId: string = '';

  constructor(private route: ActivatedRoute) {}

  ngOnInit(): void {
    this.datasetId = this.route.snapshot.paramMap.get('datasetId') || '';
  }
}
