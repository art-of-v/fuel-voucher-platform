
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
import PaymentSuccessPage from "@/pages/payment-success";
import PaymentCancelPage from "@/pages/payment-cancel";
import PaymentPage from "@/pages/payment";

import MockPayment from "@/pages/MockPayment";

import { ProtectedRoute } from "@/components/ProtectedRoute";

function Router() {
  return (
    <Layout>
      <Switch>
        {/* Public route - Login page */}
        <Route path="/profile" component={ProfileScreen} />

        {/* Protected routes - Require authentication */}
        <Route path="/">
          <ProtectedRoute>
            <StationsScreen />
          </ProtectedRoute>
        </Route>
        <Route path="/map">
          <ProtectedRoute>
            <StationsMap />
          </ProtectedRoute>
        </Route>
        <Route path="/station/:id">
          <ProtectedRoute>
            <FuelSelectionScreen />
          </ProtectedRoute>
        </Route>
        <Route path="/packages">
          <ProtectedRoute>
            <PackagesScreen />
          </ProtectedRoute>
        </Route>
        <Route path="/basket">
          <ProtectedRoute>
            <BasketScreen />
          </ProtectedRoute>
        </Route>
        <Route path="/checkout">
          <ProtectedRoute>
            <CheckoutScreen />
          </ProtectedRoute>
        </Route>
        <Route path="/payment">
          <ProtectedRoute>
            <PaymentPage />
          </ProtectedRoute>
        </Route>
        <Route path="/payment-success">
          <ProtectedRoute>
            <PaymentSuccessPage />
          </ProtectedRoute>
        </Route>
        <Route path="/payment-cancel">
          <ProtectedRoute>
            <PaymentCancelPage />
          </ProtectedRoute>
        </Route>
        <Route path="/mock-payment">
          <ProtectedRoute>
            <MockPayment />
          </ProtectedRoute>
        </Route>
        <Route path="/success">
          <ProtectedRoute>
            <SuccessScreen />
          </ProtectedRoute>
        </Route>
        <Route path="/my-codes">
          <ProtectedRoute>
            <MyCodesScreen />
          </ProtectedRoute>
        </Route>
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
