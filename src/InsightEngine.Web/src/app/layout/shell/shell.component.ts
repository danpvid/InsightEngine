import { Component } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { LanguageCode } from '../../core/models/language.model';
import { LanguageService } from '../../core/services/language.service';
import { TranslatePipe } from '../../core/pipes/translate.pipe';
import { Router } from '@angular/router';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet,
    RouterLink,
    FormsModule,
    TranslatePipe,
    MatToolbarModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatSelectModule
  ],
  templateUrl: './shell.component.html',
  styleUrls: ['./shell.component.scss']
})
export class ShellComponent {
  constructor(
    private languageService: LanguageService,
    private router: Router
  ) {}

  get currentLanguage(): LanguageCode {
    const firstSegment = this.router.url
      .split('?')[0]
      .split('#')[0]
      .split('/')
      .filter(segment => segment.length > 0)[0];

    if (this.languageService.isSupportedLanguage(firstSegment)) {
      return firstSegment;
    }

    return this.languageService.currentLanguage;
  }

  get newDatasetLink(): string[] {
    return ['/', this.currentLanguage, 'datasets', 'new'];
  }

  async onLanguageChange(language: string): Promise<void> {
    await this.languageService.switchLanguage(language, this.router);
  }
}
