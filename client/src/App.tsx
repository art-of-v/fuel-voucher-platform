
import { Switch, Route } from "wouter";
import { queryClient } from "./lib/queryClient";
import { QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "@/components/ui/toaster";
import { Toaster as SonnerToaster } from "sonner";
import { TooltipProvider } from "@/components/ui/tooltip";
import NotFound from "@/pages/not-found";
import Layout from "@/components/layout";

// Pages
import StationsScreen from "@/pages/stations";
import StationsMap from "@/pages/map";
import FuelSelectionScreen from "@/pages/fuel-selection";
import PackagesScreen from "@/pages/packages";
import BasketScreen from "@/pages/basket";
import CheckoutScreen from "@/pages/checkout";
import SuccessScreen from "@/pages/success";
import MyCodesScreen from "@/pages/my-codes";
import ProfileScreen from "@/pages/profile";

function Router() {
  return (
    <Layout>
      <Switch>
        <Route path="/" component={StationsScreen} />
        <Route path="/map" component={StationsMap} />
        <Route path="/station/:id" component={FuelSelectionScreen} />
        <Route path="/packages" component={PackagesScreen} />
        <Route path="/basket" component={BasketScreen} />
        <Route path="/checkout" component={CheckoutScreen} />
        <Route path="/success" component={SuccessScreen} />
        <Route path="/my-codes" component={MyCodesScreen} />
        <Route path="/profile" component={ProfileScreen} />
        <Route component={NotFound} />
      </Switch>
    </Layout>
  );
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <Toaster />
        <SonnerToaster position="top-center" theme="dark" />
        <Router />
      </TooltipProvider>
    </QueryClientProvider>
  );
}

export default App;
