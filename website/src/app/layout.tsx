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
    'FuelFlow тримає контур пального: договори з паливними провайдерами, пул і розподіл між водіями, звірка та документи для бухгалтерії. Для водія — код на пально за фіксованою ціною.',
  keywords: [
    'FuelFlow',
    'сервіс для транспортного бізнесу',
    'пальне для бізнесу',
    'паливний пул компанії',
    'договори з паливними провайдерами',
    'розподіл пальна між водіями',
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
    title: 'FuelFlow — пально, звірка, документи',
    description:
      'Сервіс для транспортного бізнесу: договори, пул, розподіл між водіями, звірка й документи. Для водія — код на пально з фіксованою ціною.',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'FuelFlow — пально, звірка, документи',
    description:
      'Сервіс для транспортного бізнесу: договори, пул, розподіл, звірка й документи.',
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
