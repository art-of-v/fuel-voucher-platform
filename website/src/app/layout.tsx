import type { Metadata, Viewport } from 'next';
// Self-hosted rather than next/font/google. The Google loader fetches the woff2
// at build time, so a runner that cannot reach fonts.gstatic.com fails the whole
// image build — and with it every deploy, since the images are built as one
// sequential job. Two latin subsets are 91 KB in total and the build becomes
// hermetic.
import localFont from 'next/font/local';
import Effects from '@/components/Effects';
import ScrollProgress from '@/components/ScrollProgress';
import './globals.css';

const inter = localFont({
  src: './fonts/Inter-latin.woff2',
  variable: '--font-sans',
  display: 'swap',
});

// Inter Tight is served by Google as a single file covering 500/700, i.e. it is
// variable — so no `weight` here, which is also what next/font/local wants (it
// passes the value straight to a `.trim()`).
const interTight = localFont({
  src: './fonts/InterTight-latin.woff2',
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