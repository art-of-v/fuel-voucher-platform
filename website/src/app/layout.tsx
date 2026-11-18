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
    default: 'FuelFlow — пальне для компаній і автопарків',
    template: '%s — FuelFlow',
  },
  description:
    'Керуйте паливним пулом компанії, видавайте талони водіям і контролюйте кожен літр. Для водіїв — пальне за зафіксованою ціною у застосунку FuelFlow.',
  keywords: [
    'FuelFlow',
    'пальне для бізнесу',
    'паливо для автопарку',
    'паливний пул компанії',
    'паливні талони',
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
    title: 'FuelFlow — пальне під контролем',
    description:
      'Паливний пул, видача водіям і контроль витрат для компаній. Цифрові талони та зафіксована ціна — для кожної поїздки.',
  },
  twitter: {
    card: 'summary_large_image',
    title: 'FuelFlow — пальне під контролем',
    description:
      'Паливний пул, видача водіям і контроль витрат для компаній та автопарків.',
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
