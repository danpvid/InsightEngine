import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { LanguageService } from '../services/language.service';

@Component({
  selector: 'app-language-redirect',
  standalone: true,
  template: ''
})
export class LanguageRedirectComponent implements OnInit {
  constructor(
    private router: Router,
    private languageService: LanguageService
  ) {}

  ngOnInit(): void {
    this.router.navigate(['/', this.languageService.currentLanguage, 'datasets', 'new'], {
      replaceUrl: true
    });
  }
}

