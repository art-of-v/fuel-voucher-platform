import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Bug,
  Building,
  Handshake,
  MapPin,
  MessageSquare,
  RefreshCw,
  Replace,
  ScrollText,
} from "lucide-react";
import { Layout } from "@/components/layout";
import AuditTab from "@/components/AuditTab";
import BrandsTab from "@/components/BrandsTab";
import ContractsTab from "@/components/ContractsTab";
import CustomerRenewalTab from "@/components/CustomerRenewalTab";
import ErrorLogsTab from "@/components/ErrorLogsTab";
import FeedbackTab from "@/components/FeedbackTab";
import HeldPaymentsTab from "@/components/HeldPaymentsTab";
import SettingsTab from "@/components/SettingsTab";
import StationNodesTab from "@/components/StationNodesTab";
import SuppliersTab from "@/components/SuppliersTab";
import VoucherExchangeTab from "@/components/VoucherExchangeTab";
import { StatementReconciliationPanel } from "@/components/StatementReconciliationPanel";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { signOut as endSession } from "@/lib/session";
import { useSession } from "@/lib/session-store";
import ImportBatchesTab from "./ImportBatchesTab";
import PurchasesTab from "./PurchasesTab";
import ReconciliationTab from "./ReconciliationTab";
import ReportsTab from "./ReportsTab";
import UsersTab from "./UsersTab";
import VouchersTab from "./VouchersTab";

/**
 * The tabs the admin can be on.
 *
 * <p>
 * The list is the guard against a stale stored tab (the removed 'stations' view used to be in
 * there): a tab that is not on the list cannot be restored from `localStorage`, whatever the last
 * version of the app left behind.
 */
const VALID_TABS = [
  "brands",
  "purchases",
  "users",
  "vouchers",
  "imports",
  "voucherExchange",
  "suppliers",
  "stationNodes",
  "customerRenewal",
  "contracts",
  "reconciliation",
  "heldPayments",
  "auditlog",
  "errorlogs",
  "feedback",
  "reports",
  "settings",
];

/**
 * The dashboard: the shell around the tabs, and the sidebar that chooses between them.
 *
 * <p>
 * Each tab is its own component with its own state and its own queries, so a screen fetches what
 * it shows and nothing else. The two counters that belong to the SHELL - the number of unplaced
 * payments and the number of vouchers needing attention - live here, because they are the reason
 * to open a tab rather than a part of one.
 */
export default function AdminDashboard() {
  const { t } = useI18n();
  const user = useSession((s) => s.user);

  const [activeTab, setActiveTab] = useState(() => {
    const stored = localStorage.getItem("admin_active_tab");
    return stored && VALID_TABS.includes(stored) ? stored : "brands";
  });

  const handleTabChange = (tab: string) => {
    setActiveTab(tab);
    localStorage.setItem("admin_active_tab", tab);
  };

  // How much money is sitting unplaced. Not just a tab label: the whole point of the tab is that
  // nobody thinks to open it, so the count has to meet them in the sidebar. Enabled on every tab,
  // because the number is the alarm.
  const { data: heldPaymentsTotal } = useQuery({
    queryKey: ["admin", "heldPayments", "count"],
    queryFn: () =>
      apiRequest<void, { total: number }>(
        "GET",
        "/api/admin/payments/unallocated?limit=1",
      ),
    refetchInterval: 60_000,
  });
  const heldPaymentCount = heldPaymentsTotal?.total ?? 0;

  // Stock vouchers lapsing/expired → nav badge count on "Заміна талонів" (#104). Polled while
  // logged in so the operator notices without opening the tab; cheap COUNT-only endpoint.
  const { data: exchangeAttentionCount = 0 } = useQuery<number>({
    queryKey: ["/api/admin/voucher-exchange/attention/count"],
    enabled: !!user,
    refetchInterval: 60_000,
    queryFn: async () => {
      const res = await apiRequest<void, { count: number }>(
        "GET",
        "/api/admin/voucher-exchange/attention/count",
      );
      return res.count ?? 0;
    },
  });

  return (
    <Layout
      activeTab={activeTab}
      onTabChange={handleTabChange}
      onLogout={async () => {
        await endSession();
      }}
      user={user}
      badges={{
        voucherExchange: exchangeAttentionCount,
        heldPayments: heldPaymentCount,
      }}
    >
      <div className="space-y-6">
        {/* Brands Tab (new consolidated view) */}
        {activeTab === "brands" && (
          <div className="animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold mb-6 flex items-center gap-2">
              <Building className="w-6 h-6 text-primary" />
              {t("nav.brands")}
            </h2>
            <BrandsTab onNavigateToImports={() => handleTabChange("imports")} />
          </div>
        )}

        {/* Station Nodes Tab (Точки АЗК — map price-radar data source) */}
        {activeTab === "stationNodes" && (
          <div className="animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold mb-6 flex items-center gap-2">
              <MapPin className="w-6 h-6 text-primary" />
              {t("nav.stationNodes")}
            </h2>
            <StationNodesTab />
          </div>
        )}

        {/* Suppliers Tab (Постачальники — external voucher issuers) */}
        {activeTab === "suppliers" && (
          <div className="animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold mb-6 flex items-center gap-2">
              <Handshake className="w-6 h-6 text-primary" />
              {t("suppliers.title")}
            </h2>
            <SuppliersTab />
          </div>
        )}

        {/* Voucher Exchange Tab (Заміна талонів — operator→brand stock renewal, #104) */}
        {activeTab === "voucherExchange" && (
          <div className="animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold mb-6 flex items-center gap-2">
              <Replace className="w-6 h-6 text-primary" />
              {t("voucherExchange.title")}
            </h2>
            <VoucherExchangeTab />
          </div>
        )}

        {/* Customer Renewal Tab (Операторська заміна талона клієнта) */}
        {activeTab === "customerRenewal" && (
          <div className="animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold mb-6 flex items-center gap-2">
              <RefreshCw className="w-6 h-6 text-primary" />
              {t("nav.customerRenewal")}
            </h2>
            <CustomerRenewalTab />
          </div>
        )}

        {/* Users Tab */}
        {activeTab === "users" && <UsersTab />}

        {/* Purchases Tab */}
        {activeTab === "purchases" && <PurchasesTab />}

        {/* Voucher Stock Tab */}
        {activeTab === "vouchers" && <VouchersTab />}

        {/* Import Batches Tab */}
        {activeTab === "imports" && <ImportBatchesTab />}

        {/* Reconciliation Tab */}
        {activeTab === "reconciliation" && <ReconciliationTab />}

        {/* Audit Log Tab */}
        {activeTab === "heldPayments" && <HeldPaymentsTab />}

        {activeTab === "auditlog" && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold flex items-center gap-2">
              <ScrollText className="w-6 h-6 text-primary" />
              {t("auditlog.title")}
            </h2>
            <AuditTab />
          </div>
        )}

        {/* Error Logs Tab */}
        {activeTab === "errorlogs" && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold flex items-center gap-2">
              <Bug className="w-6 h-6 text-destructive" />
              {t("errorlogs.title")}
            </h2>
            <ErrorLogsTab />
          </div>
        )}

        {/* Feedback Tab */}
        {activeTab === "feedback" && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <h2 className="text-2xl font-bold flex items-center gap-2">
              <MessageSquare className="w-6 h-6 text-primary" />
              {t("feedback.title")}
            </h2>
            <FeedbackTab />
          </div>
        )}

        {/* Settings Tab */}
        {activeTab === "settings" && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <SettingsTab />
          </div>
        )}

        {/* Reports Tab */}
        {activeTab === "reports" && <ReportsTab />}

        {/* The acquirer statement act belongs with the reports: it is the one document in this
            system that compares us against an independent source, and it has existed as an API
            since #118 landed with no way to reach it. Sits above the generated report so it does
            not depend on that form having been filled in. */}
        {activeTab === "reports" && <StatementReconciliationPanel />}

        {/* Contracts Tab */}
        {activeTab === "contracts" && (
          <div className="space-y-6 animate-in fade-in duration-300">
            <ContractsTab />
          </div>
        )}
      </div>
    </Layout>
  );
}
