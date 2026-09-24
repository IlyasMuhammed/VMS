import { DestroyRef, Signal, computed, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, catchError, debounceTime, map, merge, switchMap, tap } from 'rxjs';
import { PartnersApi } from '../../core/api.services';
import { DuplicateCheckRequest } from '../../core/partner.models';
import { PartnerForm } from './partner-form';
import { DuplicateMatch, canSaveDespite, isCnic, isNtn, matchesToAcknowledge, normalizeMobile } from './partner-logic';

/** The fields whose values decide whether a partner looks like another. */
export interface DuplicateBasis {
  legalName: string;
  cnic: string;
  ntn: string;
  primaryMobile: string;
  cityId: number | null;
}

export interface DuplicateWatcher {
  /** Partners that look like the one being entered, refreshed a moment after the person stops typing. */
  readonly matches: Signal<readonly DuplicateMatch[]>;
  /** Whether the person has ticked "this is a different partner". Falls back to false whenever the list changes. */
  readonly acknowledged: ReturnType<typeof signal<boolean>>;
  /** True when nothing blocks and every candidate that needs an answer has one. */
  readonly canSave: Signal<boolean>;
  /** The ids to send as `acknowledgedDuplicateIds`. */
  ackIds(): number[];
  /** Asks again now, for after a save was refused for a duplicate the panel had not shown. */
  refresh(): void;
  /** Forgets what was found (for Save and New). */
  clear(): void;
}

const basisOf = (form: PartnerForm): DuplicateBasis => {
  const v = form.getRawValue();
  return { legalName: v.legalName.trim(), cnic: v.cnic.trim(), ntn: v.ntn.trim(), primaryMobile: v.primaryMobile.trim(), cityId: v.cityId };
};

/**
 * Watches the identifying fields and asks the API what already exists (FSD §12.1), so the person is told while typing, not
 * after pressing Save. The save asks again on the server, so nothing depends on this having run.
 *
 * @param baseline For an existing partner: what it was when loaded. Nothing is asked until one of these values changes,
 *   so opening an old record that has a known twin does not nag; null asks about whatever is typed (a new partner).
 */
export function watchDuplicates(
  api: PartnersApi,
  form: PartnerForm,
  destroyRef: DestroyRef,
  options: { excludeId: () => number | null; baseline: () => DuplicateBasis | null },
): DuplicateWatcher {
  const matches = signal<readonly DuplicateMatch[]>([]);
  const acknowledged = signal(false);
  let lastKey = '';

  const changed = (now: DuplicateBasis, was: DuplicateBasis | null): boolean =>
    !was || now.legalName !== was.legalName || now.cnic !== was.cnic || now.ntn !== was.ntn || now.primaryMobile !== was.primaryMobile || now.cityId !== was.cityId;

  const requestOf = (): DuplicateCheckRequest | null => {
    const now = basisOf(form);
    if (!changed(now, options.baseline())) return null;
    const mobile = normalizeMobile(now.primaryMobile);
    const request: DuplicateCheckRequest = {
      partyType: form.controls.partyType.value,
      legalName: now.legalName.length >= 3 ? now.legalName : null,
      cnic: isCnic(now.cnic) ? now.cnic : null,
      ntn: isNtn(now.ntn) ? now.ntn : null,
      primaryMobile: mobile,
      cityId: now.cityId,
      excludeId: options.excludeId(),
    };
    return request.legalName || request.cnic || request.ntn || request.primaryMobile ? request : null;
  };

  const run = () => {
    const request = requestOf();
    if (!request) {
      matches.set([]);
      return EMPTY;
    }
    return api.duplicateCheck(request).pipe(catchError(() => EMPTY));
  };

  const trigger = merge(
    form.controls.partyType.valueChanges,
    form.controls.legalName.valueChanges,
    form.controls.cnic.valueChanges,
    form.controls.ntn.valueChanges,
    form.controls.primaryMobile.valueChanges,
    form.controls.cityId.valueChanges,
  );

  trigger
    .pipe(
      debounceTime(500),
      map(() => 0),
      switchMap(() => run()),
      tap((found) => {
        // A different list needs a fresh answer: the tick is for the candidates the person was looking at.
        const key = found.map((m) => `${m.id}:${m.matchType}`).join(',');
        if (key !== lastKey) acknowledged.set(false);
        lastKey = key;
        matches.set(found);
      }),
      takeUntilDestroyed(destroyRef),
    )
    .subscribe();

  return {
    matches,
    acknowledged,
    canSave: computed(() => canSaveDespite(matches(), acknowledged())),
    ackIds: () => matchesToAcknowledge(matches()).map((m) => m.id),
    refresh: () => {
      const request = requestOf();
      if (request) api.duplicateCheck(request).subscribe({ next: (found) => matches.set(found), error: () => undefined });
    },
    clear: () => {
      lastKey = '';
      matches.set([]);
      acknowledged.set(false);
    },
  };
}
