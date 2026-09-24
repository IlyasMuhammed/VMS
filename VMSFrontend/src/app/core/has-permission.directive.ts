import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { canAccess } from './access';
import { AuthService } from './auth.service';

/**
 * Shows part of a template only to a user who holds the permission (operation-level access, FSD §23B):
 * `<button *vmsCan="'USER_MANAGE'">Invite</button>`, or `*vmsCan="['VEH_EDIT', 'VEH_CREATE']"` for any of several.
 * This is a courtesy, not security: the server refuses the request whatever the screen shows.
 */
@Directive({ selector: '[vmsCan]', standalone: true })
export class HasPermissionDirective {
  private readonly auth = inject(AuthService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly view = inject(ViewContainerRef);
  private shown = false;

  readonly vmsCan = input.required<string | readonly string[]>();

  constructor() {
    effect(() => {
      const wanted = this.vmsCan();
      const allowed = canAccess(typeof wanted === 'string' ? { permission: wanted } : { anyPermission: wanted }, this.auth.user());
      if (allowed && !this.shown) {
        this.view.createEmbeddedView(this.template);
        this.shown = true;
      } else if (!allowed && this.shown) {
        this.view.clear();
        this.shown = false;
      }
    });
  }
}
