import type { Metadata, Viewport } from 'next';
import { Inter, Inter_Tight } from 'next/font/google';
import Effects from '@/components/Effects';
import ScrollProgress from '@/components/ScrollProgress';
import './globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-sans',
  display: 'swap',
});

const interTight = Inter_Tight({
  weight: ['500', '600', '700'],
  subsets: ['latin'],
  variable: '--font-display',
  display: 'swap',
});

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
    <html
      lang="uk"
      className={`${inter.variable} ${interTight.variable}`}
    >
      <body>
        <ScrollProgress />
        <Effects />
        {children}
      </body>
    </html>
  );
}