import Header from '@/components/Header';
import Hero from '@/components/Hero';
import How from '@/components/How';
import Numbers from '@/components/Numbers';
import AppShowcase from '@/components/AppShowcase';
import Prices from '@/components/Prices';
import Partners from '@/components/Partners';
import Business from '@/components/Business';
import Gallery from '@/components/Gallery';
import Manifesto from '@/components/Manifesto';
import FAQ from '@/components/FAQ';
import Final from '@/components/Final';
import Contact from '@/components/Contact';
import Footer from '@/components/Footer';

export default function Home() {
  return (
    <>
      <Header />
      <main>
        <Hero />
        <How />
        <Numbers />
        <AppShowcase />
        <Prices />
        <Partners />
        <Business />
        <Gallery />
        <Manifesto />
        <FAQ />
        <Final />
        <Contact />
      </main>
      <Footer />
    </>
  );
}