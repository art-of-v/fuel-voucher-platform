import { useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";

/**
 * Starts the catalogue check that the networks' price lists feed.
 *
 * <p>
 * The check runs on its own every week, because whether a vendor sells a grade we do not carry is
 * news on a scale of weeks. This starts it now, for the week somebody emptied a catalogue and asked
 * for it back.
 *
 * <p>
 * The endpoint ENQUEUES the jobs rather than running them: each one fetches a third party's page, and
 * a request that waited on three of those would hold a connection open for as long as the slowest
 * vendor takes to answer. So the button reports that the check started, and what it changed shows up
 * on the brands screen a minute later.
 */
export default function CatalogueSyncButton() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: async () => {
      await apiRequest("POST", "/api/admin/catalogue/sync");
    },
    onSuccess: () => {
      toast.success(t("brands.syncCatalogueStarted"));
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands"] });
    },
    onError: () => {
      toast.error(t("brands.syncCatalogueFailed"));
    },
  });

  return (
    <Button
      variant="outline"
      size="sm"
      onClick={() => mutation.mutate()}
      disabled={mutation.isPending}
      title={t("brands.syncCatalogueHint")}
    >
      <RefreshCw
        className={
          "w-3.5 h-3.5 mr-1" + (mutation.isPending ? " animate-spin" : "")
        }
      />
      {t("brands.syncCatalogue")}
    </Button>
  );
}
