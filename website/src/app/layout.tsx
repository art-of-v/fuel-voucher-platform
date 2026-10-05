import type { Metadata, Viewport } from 'next';
import Effects from '@/components/Effects';
import ScrollProgress from '@/components/ScrollProgress';
// The typefaces are declared by hand at the top of globals.css, not through
// next/font: next/font/local cannot express unicode-range, which is what splits
// the Cyrillic subset away from the Latin one. See the comment there.
import './globals.css';

const siteUrl = 'https://palne.shop';

export const viewport: Viewport = {
  themeColor: '#000000',
  width: 'device-width',
  initialScale: 1,
};

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: {
    default: 'FuelFlow — Купуй паливо сьогодні. Зафіксуй ціну.',
    template: '%s — FuelFlow',
  },
  description:
    'Цифровий паливний гаманець. Купуй літри за сьогоднішньою ціною, зберігай у телефоні, заправляйся за QR на OKKO, WOG, KLO, UPG.',
  keywords: [
    'FuelFlow',
    'паливні талони',
    'цифровий паливний гаманець',
    'фіксована ціна пального',
    'заправка за QR',
    'OKKO',
    'WOG',
    'KLO',
    'UPG',
    'пальне за фіксованою ціною',
    'паливо для компаній',
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
    title: 'FuelFlow — Купуй паливо сьогодні. Зафіксуй ціну.',
    description:
      'Пальне вже у твоєму телефоні. Купуй літри за сьогоднішньою ціною і заправляйся за QR.',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'FuelFlow — Купуй паливо сьогодні. Зафіксуй ціну.',
    description:
      'Пальне вже у твоєму телефоні. Купуй літри за сьогоднішньою ціною і заправляйся за QR.',
  },
  robots: { index: true, follow: true },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="uk">
      <head>
        {/* The Cyrillic subsets carry every heading and most body copy, so they
            are the ones worth fetching before first paint. Latin waits for the
            browser to ask — next/font used to preload it, which is the one
            nicety lost by declaring the faces by hand. */}
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
      <body>
        <ScrollProgress />
        <Effects />
        {children}
      </body>
    </html>
  );
}