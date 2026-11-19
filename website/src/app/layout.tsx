import type { Metadata, Viewport } from 'next';
import './globals.css';

const siteUrl = 'https://fuelflowmarketplace.com';

export const viewport: Viewport = {
  themeColor: '#030403',
  width: 'device-width',
  initialScale: 1,
};

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: {
    default: 'FuelFlow — сервіс для транспортного бізнесу',
    template: '%s — FuelFlow',
  },
  description:
    'Договори з постачальниками, закуплене пальне, коди для водіїв, звірка витрат і документи для бухгалтерії в одному застосунку. Для водія код на пальне з фіксованою ціною.',
  keywords: [
    'FuelFlow',
    'сервіс для транспортного бізнесу',
    'пальне для бізнесу',
    'закупівля пального для автопарку',
    'договори з паливними постачальниками',
    'розподіл пального між водіями',
    'акти звірки',
    'цифровий паливний гаманець',
    'фіксована ціна пального',
    'OKKO',
    'WOG',
    'KLO',
    'UPG',
  ],
  authors: [{ name: 'FuelFlow' }],
  creator: 'FuelFlow',
  publisher: 'FuelFlow',
  alternates: { canonical: siteUrl },
  openGraph: {
    type: 'website',
    locale: 'uk_UA',
    url: siteUrl,
    siteName: 'FuelFlow',
    title: 'FuelFlow — паливний бізнес під контролем',
    description:
      'Договори з постачальниками, закуплене пальне, коди для водіїв, звірка витрат і документи для бухгалтерії в одному застосунку',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'FuelFlow — паливний бізнес під контролем',
    description:
      'Договори, закуплення, розподіл, звірка й документи для бухгалтерії в одному застосунку',
  },
  robots: { index: true, follow: true },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="uk">
      <head>
        <link
          rel="preload"
          href="/fonts/InterTight-cyrillic.woff2"
          as="font"
          type="font/woff2"
          crossOrigin="anonymous"
        />
        <link
          rel="preload"
          href="/fonts/Inter-cyrillic.woff2"
          as="font"
          type="font/woff2"
          crossOrigin="anonymous"
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
