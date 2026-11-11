const { test } = require('node:test');
const assert = require('node:assert/strict');
const { format } = require('../../Algara.Web/wwwroot/js/client-time.js');
test('saved client zone formats winter time and DD.MM.YYYY', () => {
    assert.equal(format('2026-11-11T12:34:00Z', 'Europe/Sofia'), '11.11.2026 14:34');
});
test('saved client zone applies daylight saving at the event date', () => {
    assert.equal(format('2026-07-11T12:34:00Z', 'Europe/Sofia'), '11.07.2026 15:34');
});
test('client timezone can change the calendar date', () => {
    assert.equal(format('2026-11-11T01:34:00Z', 'America/New_York'), '10.11.2026 20:34');
});
test('UTC is explicit and midnight is 00 rather than 24', () => {
    assert.equal(format('2026-11-11T00:00:00Z', 'UTC'), '11.11.2026 00:00');
});
test('invalid date or unsupported zone leaves server fallback intact', () => {
    assert.equal(format('not a date', 'UTC'), null);
    assert.equal(format('2026-11-11T00:00:00Z', 'Invalid/Zone'), null);
});
