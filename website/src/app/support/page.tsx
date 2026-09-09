import type { Metadata } from 'next';
import SupportClient from '@/components/SupportClient';

export const metadata: Metadata = {
  title: 'Підтримка',
  description:
    'Питання щодо оплати, талонів або застосунку FuelFlow? Напишіть нам — відповідаємо з palne.shopua@gmail.com.',
  alternates: { canonical: '/support' },
};

export default function SupportPage() {
  return <SupportClient />;
}
