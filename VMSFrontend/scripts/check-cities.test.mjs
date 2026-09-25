// Tests for the framework-free rules of the City screen (FSD §16).
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cityDisplay, isAbbreviation, statusSeverity } from '../src/app/pages/cities/city-logic.ts';

test('status chip severity', () => {
  assert.equal(statusSeverity('Active'), 'success');
  assert.equal(statusSeverity('Inactive'), 'danger');
});

test('a city always displays as "Name (ABBR)"', () => {
  assert.equal(cityDisplay('Lahore', 'LHR'), 'Lahore (LHR)');
});

test('abbreviation format: 2-5 letters', () => {
  assert.equal(isAbbreviation('LHR'), true);
  assert.equal(isAbbreviation('FSD'), true);
  assert.equal(isAbbreviation('L'), false);
  assert.equal(isAbbreviation('TOOLONG'), false);
  assert.equal(isAbbreviation('12'), false);
});
