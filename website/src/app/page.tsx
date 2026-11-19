import Header from '@/components/Header';
import Hero from '@/components/Hero';
import Services from '@/components/Services';
import Business from '@/components/Business';
import AppShowcase from '@/components/AppShowcase';
import Prices from '@/components/Prices';
import Contact from '@/components/Contact';
import How from '@/components/How';
import FAQ from '@/components/FAQ';
import Final from '@/components/Final';
import Footer from '@/components/Footer';

export default function Home() {
  return (
    <>
      <Header />
      <main>
        <Hero />
        <Services />
        <Business />
        <AppShowcase />
        <Prices />
        <Contact />
        <How />
        <FAQ />
        <Final />
      </main>
      <Footer />
    </>
  );
}
