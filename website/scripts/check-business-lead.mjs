#!/usr/bin/env node

import { launchBrowser, report, requireBrowser, serveOut } from './lib/harness.mjs';

const puppeteer = await requireBrowser();
const site = await serveOut();
const browser = await launchBrowser(puppeteer);
const failures = [];

let responseMode = 'accepted';
let lastPayload = null;
let leadRequests = 0;

const textIncludes = async (page, selector, expected) => {
  const text = await page.$eval(selector, (element) => element.textContent || '');
  return text.includes(expected);
};

const fillValid = async (page) => {
  await page.type('#lead-company', 'ТОВ Тестовий автопарк');
  await page.type('#lead-contact', 'Іван Петренко');
  await page.type('#lead-phone', '+380 (97) 001-17-71');
  await page.type('#lead-email', 'fleet@example.com');
  await page.type('#lead-fleet', '25');
  await page.type('#lead-message', 'Близько 8000 л на місяць');
};

const openForm = async (page) => {
  await page.goto(`${site.origin}/`, { waitUntil: 'networkidle0' });
  await page.evaluate(() => document.fonts.ready);
  await page.$eval('#business-lead', (element) => element.scrollIntoView());
};

try {
  const page = await browser.newPage();
  await page.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
  await page.setRequestInterception(true);
  page.on('request', (request) => {
    const url = request.url();
    if (url.endsWith('/api/packages')) {
      request.respond({ status: 200, contentType: 'application/json', body: '[]' });
      return;
    }
    if (!url.endsWith('/api/business-leads')) {
      request.continue();
      return;
    }

    const cors = {
      'access-control-allow-origin': '*',
      'access-control-allow-methods': 'POST, OPTIONS',
      'access-control-allow-headers': 'content-type',
    };
    if (request.method() === 'OPTIONS') {
      request.respond({ status: 204, headers: cors });
      return;
    }

    leadRequests += 1;
    lastPayload = JSON.parse(request.postData() || '{}');
    if (responseMode === 'offline') {
      request.abort('failed');
      return;
    }
    if (responseMode === 'validation') {
      request.respond({
        status: 400,
        headers: cors,
        contentType: 'application/json',
        body: JSON.stringify({ errors: { Phone: ['Перевірте номер телефону.'] } }),
      });
      return;
    }
    if (responseMode === 'limited') {
      request.respond({ status: 429, headers: cors, contentType: 'application/json', body: '{}' });
      return;
    }
    if (responseMode === 'server') {
      request.respond({ status: 500, headers: cors, contentType: 'application/json', body: '{}' });
      return;
    }
    request.respond({
      status: 202,
      headers: cors,
      contentType: 'application/json',
      body: JSON.stringify({ id: '019aa111-1111-7111-8111-111111111111' }),
    });
  });

  await openForm(page);
  const privacyHref = await page.$eval('#business-lead a[href="/privacy/"]', (element) =>
    element.getAttribute('href'),
  );
  if (privacyHref !== '/privacy/') failures.push('lead form does not link to the privacy policy');
  await page.type('#lead-company', 'ТОВ Тест');
  await page.type('#lead-contact', 'Іван');
  await page.type('#lead-phone', '0970011771');
  const beforeInvalid = leadRequests;
  await page.click('#business-lead button[type="submit"]');
  await page.waitForSelector('#lead-phone-error');
  if (leadRequests !== beforeInvalid) failures.push('client-invalid phone reached the API');
  if ((await page.$eval('#lead-phone', (element) => element.getAttribute('aria-invalid'))) !== 'true') {
    failures.push('client-invalid phone is not exposed through aria-invalid');
  }

  responseMode = 'validation';
  await openForm(page);
  await fillValid(page);
  await page.click('#business-lead button[type="submit"]');
  await page.waitForSelector('#lead-phone-error');
  if (!(await textIncludes(page, '#lead-phone-error', 'Перевірте номер'))) {
    failures.push('400 field error is not rendered beside the matching field');
  }
  if ((await page.$eval('#lead-company', (element) => element.value)) !== 'ТОВ Тестовий автопарк') {
    failures.push('400 response cleared the submitted company name');
  }

  responseMode = 'limited';
  await openForm(page);
  await fillValid(page);
  await page.click('#business-lead button[type="submit"]');
  await page.waitForSelector('#business-lead [role="alert"]');
  if (!(await textIncludes(page, '#business-lead [role="alert"]', 'Забагато заявок'))) {
    failures.push('429 response does not explain the rate limit');
  }

  responseMode = 'server';
  await openForm(page);
  await fillValid(page);
  await page.click('#business-lead button[type="submit"]');
  await page.waitForSelector('#business-lead [role="alert"]');
  if (!(await textIncludes(page, '#business-lead [role="alert"]', 'Не вдалося'))) {
    failures.push('5xx response does not render a retryable error');
  }

  responseMode = 'offline';
  await openForm(page);
  await fillValid(page);
  await page.click('#business-lead button[type="submit"]');
  await page.waitForSelector('#business-lead [role="alert"]');
  if (!(await textIncludes(page, '#business-lead [role="alert"]', 'Немає зʼєднання'))) {
    failures.push('network failure does not render the offline state');
  }

  responseMode = 'accepted';
  await openForm(page);
  await fillValid(page);
  await page.click('#business-lead button[type="submit"]');
  await page.waitForSelector('#business-lead [role="status"]');
  if (!(await textIncludes(page, '#business-lead [role="status"]', 'Заявку прийнято'))) {
    failures.push('202 response does not render the accepted state');
  }
  if (
    lastPayload?.companyName !== 'ТОВ Тестовий автопарк' ||
    lastPayload?.contactName !== 'Іван Петренко' ||
    lastPayload?.phone !== '+380970011771' ||
    lastPayload?.email !== 'fleet@example.com' ||
    lastPayload?.fleetSize !== 25 ||
    lastPayload?.message !== 'Близько 8000 л на місяць' ||
    lastPayload?.website !== ''
  ) {
    failures.push(`accepted request has the wrong payload: ${JSON.stringify(lastPayload)}`);
  }

  await page.close();
} finally {
  await browser.close();
  site.close();
}

report(
  'business-lead',
  failures,
  `${leadRequests} API submissions covered client validation, 202, 400, 429, 5xx and offline states.`,
);
