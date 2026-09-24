import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/**
 * The browser tab and history say where you are ("Users · VMS"), and a screen reader announces it on every
 * page change. Give a route a `title` and this does the rest.
 */
@Injectable({ providedIn: 'root' })
export class VmsTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const page = this.buildTitle(snapshot);
    this.title.setTitle(page ? `${page} · VMS` : 'VMS');
  }
}
