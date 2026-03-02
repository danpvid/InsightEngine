import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute } from '@angular/router';
import { ApiResponse } from '../../../../core/models/api-response.model';
import { AuthService } from '../../../../core/services/auth.service';
import { environment } from '../../../../../environments/environment.development';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-profile-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatTabsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule
  ],
  templateUrl: './profile-page.component.html',
  styleUrls: ['./profile-page.component.scss']
})
export class ProfilePageComponent implements OnInit {
  selectedIndex = 0;
  saving = false;
  plan = 'Free';

  readonly profileForm = this.fb.group({
    displayName: ['', [Validators.required, Validators.maxLength(200)]]
  });

  readonly passwordForm = this.fb.group({
    currentPassword: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(8)]]
  });

  readonly avatarForm = this.fb.group({
    avatarUrl: ['', [Validators.required]]
  });

  constructor(
    private readonly fb: FormBuilder,
    private readonly authService: AuthService,
    private readonly http: HttpClient,
    private readonly route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    const user = this.authService.currentUser;
    this.profileForm.patchValue({ displayName: user?.displayName ?? '' });
    this.avatarForm.patchValue({ avatarUrl: user?.avatarUrl ?? '' });
    this.loadPlan();

    const tab = (this.route.snapshot.queryParamMap.get('tab') || '').toLowerCase();
    if (tab === 'plan') {
      this.selectedIndex = 3;
    }
  }

  saveProfile(): void {
    if (this.profileForm.invalid || this.saving) {
      return;
    }

    this.saving = true;
    this.http.put<ApiResponse<any>>(`${environment.apiBaseUrl}/api/v1/me/profile`, this.profileForm.getRawValue()).subscribe({
      next: (response) => {
        this.saving = false;
        if (response.data) {
          this.authService.mergeUser(response.data);
        }
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  savePassword(): void {
    if (this.passwordForm.invalid || this.saving) {
      return;
    }

    this.saving = true;
    this.http.put<ApiResponse<boolean>>(`${environment.apiBaseUrl}/api/v1/me/password`, this.passwordForm.getRawValue()).subscribe({
      next: () => {
        this.saving = false;
        this.passwordForm.reset();
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  saveAvatar(): void {
    if (this.avatarForm.invalid || this.saving) {
      return;
    }

    this.saving = true;
    this.http.put<ApiResponse<any>>(`${environment.apiBaseUrl}/api/v1/me/avatar`, this.avatarForm.getRawValue()).subscribe({
      next: (response) => {
        this.saving = false;
        if (response.data) {
          this.authService.mergeUser(response.data);
        }
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  upgradePlan(): void {
    this.saving = true;
    this.http.post<ApiResponse<{ plan: string }>>(`${environment.apiBaseUrl}/api/v1/me/plan/upgrade`, { targetPlan: 'Pro' }).subscribe({
      next: () => {
        this.saving = false;
        this.loadPlan();
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  private loadPlan(): void {
    this.http.get<ApiResponse<{ plan: string }>>(`${environment.apiBaseUrl}/api/v1/me/plan`).subscribe({
      next: (response) => {
        this.plan = response.data?.plan ?? 'Free';
      }
    });
  }
}
