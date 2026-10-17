import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize, take } from 'rxjs';

import { AuthSessionFacade } from '../../../core/auth/auth-session.facade';
import { I18nService } from '../../../core/i18n/i18n.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-login-page',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
})
export class LoginPageComponent {
  private readonly authSession = inject(AuthSessionFacade);
  readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

  readonly isSubmitting = signal(false);
  private readonly loginFailed = signal(false);
  readonly errorMessage = computed(() => this.loginFailed() ? this.i18n.translate('login.failure') : null);
  readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email]
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required]
    })
  });

  submit(): void {
    this.loginFailed.set(false);
    if (this.form.invalid || this.isSubmitting()) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password } = this.form.getRawValue();
    this.isSubmitting.set(true);
    this.authSession
      .login(email.trim(), password)
      .pipe(
        take(1),
        finalize(() => this.isSubmitting.set(false))
      )
      .subscribe({
        next: () => {
          void this.router.navigateByUrl('/workspaces');
        },
        error: () => {
          this.loginFailed.set(true);
        }
      });
  }
}
