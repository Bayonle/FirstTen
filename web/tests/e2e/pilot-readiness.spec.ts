import { expect, test } from '@playwright/test'

test('anonymous operator sees no incident data and can reach secure sign-in', async ({ page }) => {
  await page.route('/api/incidents', (route) => route.fulfill({ status: 401, body: '{}' }))
  await page.route('/hubs/operations/**', (route) => route.abort())
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'See the next action, not the noise.' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Your operational session is not active.' })).toBeVisible()
  await page.getByRole('link', { name: 'Sign in securely' }).click()
  await expect(page.getByRole('heading', { name: 'Enter the control room.' })).toBeVisible()
})

test('narrow queue keeps navigation and primary heading keyboard reachable', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.route('/api/incidents', (route) => route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }))
  await page.route('/hubs/operations/**', (route) => route.abort())
  await page.goto('/')
  await expect(page.getByRole('navigation', { name: 'Product modules' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'See the next action, not the noise.' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Listening, with no active report.' })).toBeVisible()
})
