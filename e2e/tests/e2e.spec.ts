import { test, expect } from '@playwright/test';

test.describe('E2E: WASM client and distributed sync', () => {
  test('page loads and canvas visible', async ({ page }) => {
    await page.goto('http://localhost:5000', { waitUntil: 'load' });
    const canvas = page.locator('#canvas');
    await expect(canvas).toBeVisible();
  });

  test('two clients synchronize via /ws (join -> snapshot -> op)', async ({ browser }) => {
    const room = `test-room-${Date.now()}`;
    const p1 = await browser.newPage();
    const p2 = await browser.newPage();

    await p1.goto('http://localhost:5000');
    await p2.goto('http://localhost:5000');

    // Create WS in page 1
    await p1.evaluate(([room, clientId]) => {
      (window as any).__room = room;
      (window as any).__wsMessages = [];
      const ws = new WebSocket(`ws://${location.host}/ws`);
      (window as any).__ws = ws;
      ws.onmessage = (ev) => (window as any).__wsMessages.push(ev.data);
      ws.onopen = () => ws.send(JSON.stringify({ type: 'join', room, clientId }));
    }, [room, 'client-1']);

    // Create WS in page 2
    await p2.evaluate(([room, clientId]) => {
      (window as any).__room = room;
      (window as any).__wsMessages = [];
      const ws = new WebSocket(`ws://${location.host}/ws`);
      (window as any).__ws = ws;
      ws.onmessage = (ev) => (window as any).__wsMessages.push(ev.data);
      ws.onopen = () => ws.send(JSON.stringify({ type: 'join', room, clientId }));
    }, [room, 'client-2']);

    // Wait for both to receive a snapshot or peer_join (server snapshot is sent on join)
    await p1.waitForFunction(() => {
      const m = (window as any).__wsMessages || [];
      return m.some((x: string) => {
        try { const j = JSON.parse(x); return j.type === 'snapshot' || j.type === 'peer_join'; } catch { return false; }
      });
    }, null, { timeout: 5000 });

    await p2.waitForFunction(() => {
      const m = (window as any).__wsMessages || [];
      return m.some((x: string) => {
        try { const j = JSON.parse(x); return j.type === 'snapshot' || j.type === 'peer_join'; } catch { return false; }
      });
    }, null, { timeout: 5000 });

    // Send an op from client-1
    await p1.evaluate(() => {
      const ws = (window as any).__ws as WebSocket;
      ws.send(JSON.stringify({ type: 'op', room: (window as any).__room, payload: { hello: 'world' } }));
    });

    // Assert client-2 receives the op
    await p2.waitForFunction(() => {
      const m = (window as any).__wsMessages || [];
      return m.some((x: string) => {
        try { const j = JSON.parse(x); return j.type === 'op' && j.payload && j.payload.hello === 'world'; } catch { return false; }
      });
    }, null, { timeout: 5000 });

    await p1.close();
    await p2.close();
  });
});