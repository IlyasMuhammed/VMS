import { Injectable, inject, signal } from '@angular/core';
import { catchError, of } from 'rxjs';
import { TenantsApi } from './api.services';
import { LogoVariant, TenantDetail } from './models';

/**
 * Holds the signed-in tenant's logos as object URLs. The logos are fetched through the API with the
 * bearer token (there is no public logo URL), so an <img> can only show them from a blob URL.
 * A logo is only refetched when its version (content hash) changes.
 */
@Injectable({ providedIn: 'root' })
export class TenantBrandingService {
  private readonly api = inject(TenantsApi);

  private readonly lightUrl = signal<string | null>(null);
  private readonly darkUrl = signal<string | null>(null);
  private readonly versions: Record<LogoVariant, string | null> = { light: null, dark: null };
  private tenantId = '';

  /** The logo drawn for light backgrounds, or null. */
  readonly light = this.lightUrl.asReadonly();
  /** The logo drawn for dark backgrounds, or null. */
  readonly dark = this.darkUrl.asReadonly();

  /** Loads whichever of the tenant's logos exist and are not already loaded. */
  apply(tenant: Pick<TenantDetail, 'id' | 'logoLightVersion' | 'logoDarkVersion'>): void {
    if (this.tenantId !== tenant.id) this.clear();
    this.tenantId = tenant.id;
    this.sync('light', tenant.logoLightVersion ?? null);
    this.sync('dark', tenant.logoDarkVersion ?? null);
  }

  /** Forgets everything and frees the blobs. Called when leaving the signed-in shell. */
  clear(): void {
    this.tenantId = '';
    for (const v of ['light', 'dark'] as const) {
      this.versions[v] = null;
      this.release(v);
    }
  }

  private sync(variant: LogoVariant, version: string | null): void {
    if (version === this.versions[variant]) return;
    this.versions[variant] = version;
    this.release(variant);
    if (!version) return;

    this.api
      .currentLogo(variant)
      .pipe(catchError(() => of(null)))
      .subscribe((blob) => {
        // Ignore a response that arrives after the version moved on or the shell was left.
        if (blob && this.versions[variant] === version) this.target(variant).set(URL.createObjectURL(blob));
      });
  }

  private target(variant: LogoVariant) {
    return variant === 'light' ? this.lightUrl : this.darkUrl;
  }

  private release(variant: LogoVariant): void {
    const url = this.target(variant)();
    if (url) URL.revokeObjectURL(url);
    this.target(variant).set(null);
  }
}
