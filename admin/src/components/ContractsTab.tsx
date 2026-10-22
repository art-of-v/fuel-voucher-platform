import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { FileSignature, FileUp, Loader2, ScrollText, X } from "lucide-react";
import { toast } from "sonner";

import { useI18n } from "@/lib/i18n";
import { apiRequest } from "@/lib/api-client";
import { useSession } from "@/lib/session-store";
import { formatDate, formatDateTime } from "@/lib/utils";
import type {
  AdminSignedContract,
  AdminSignedContractSignature,
  PagedResult,
} from "@/lib/api-types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { SignatureViewer } from "@/components/SignatureViewer";

/** A contract, as `GET /api/admin/legal-entity/contracts` answers. */
type AdminContract = {
  id: string;
  brandId: string;
  brandName: string | null;
  legalEntityId: string;
  companyName: string | null;
  userId: string;
  customerName: string | null;
  createdAtUtc: string;
  expiresAtUtc: string;
  expired: boolean;
  termsVersion: number;
  signed: boolean;
  signedAtUtc: string | null;
};

type Brand = { id: string; name: string };

/** One version of a provider's text, as `GET /api/admin/contract-versions` answers. */
type ContractVersion = {
  id: string;
  brandId: string;
  version: number;
  body: string;
  termDays: number;
  createdAtUtc: string;
};

/** The term of a contract, in days, when the operator has not said otherwise. */
const DEFAULT_TERM_DAYS = 365;

/** The signed contracts table shows fifty rows at a time; the endpoint is paged to match. */
const SIGNED_PAGE_SIZE = 50;

export default function ContractsTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const user = useSession((s) => s.user);

  const [brandId, setBrandId] = useState<string>("");
  const [termDays, setTermDays] = useState<string>(String(DEFAULT_TERM_DAYS));
  const [file, setFile] = useState<File | null>(null);
  const [signedPage, setSignedPage] = useState(1);
  const [signatureId, setSignatureId] = useState<string | null>(null);

  const { data: brands = [] } = useQuery<Brand[]>({
    queryKey: ["/api/admin/brands"],
  });

  const { data: contracts = [] } = useQuery<AdminContract[]>({
    queryKey: ["/api/admin/legal-entity/contracts"],
  });

  // The signed contracts are a PAGE, and the list carries no signature image: the image is
  // fetched per record, on demand, by the query below.
  const { data: signedContractsPage } = useQuery<PagedResult<AdminSignedContract>>({
    queryKey: ["/api/admin/legal-entity/contracts/signed-contracts", signedPage],
    queryFn: () =>
      apiRequest<void, PagedResult<AdminSignedContract>>(
        "GET",
        `/api/admin/legal-entity/contracts/signed-contracts?page=${signedPage}&limit=${SIGNED_PAGE_SIZE}`,
      ),
  });

  const signedContracts = signedContractsPage?.items ?? [];
  const signedTotalPages = signedContractsPage?.totalPages ?? 0;

  // The image itself. It is behind the SignaturePayload capability on the server, and the row
  // it writes says who opened it - so it is fetched when the operator asks for it, not with the
  // list.
  const { data: signature, isLoading: isSignatureLoading } =
    useQuery<AdminSignedContractSignature | null>({
      queryKey: ["/api/admin/legal-entity/contracts/signed-contracts", signatureId],
      queryFn: async () => {
        if (!signatureId) return null;
        return await apiRequest<void, AdminSignedContractSignature>(
          "GET",
          `/api/admin/legal-entity/contracts/signed-contracts/${signatureId}`,
        );
      },
      enabled: !!signatureId,
    });

  const { data: versions = [] } = useQuery<ContractVersion[]>({
    queryKey: ["/api/admin/contract-versions", brandId],
    queryFn: () =>
      apiRequest<unknown, ContractVersion[]>(
        "GET",
        `/api/admin/contract-versions?brandId=${brandId}`,
      ),
    enabled: !!brandId,
  });

  const publish = useMutation({
    mutationFn: async () => {
      if (!file) throw new Error(t("contracts.selectDocument"));

      const fd = new FormData();
      fd.append("brandId", brandId);
      fd.append("termDays", termDays);
      fd.append("file", file);

      return apiRequest<
        unknown,
        { version: number; characters: number; contractsCreated: number }
      >(
        "POST",
        "/api/admin/contract-versions/upload",
        fd,
        undefined,
        120_000,
        0,
      );
    },
    onSuccess: (res) => {
      toast.success(t("contracts.published", String(res.version)));
      setFile(null);
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/contract-versions"],
      });
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/legal-entity/contracts"],
      });
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const term = Number(termDays);
  const canPublish =
    !!brandId &&
    !!file &&
    Number.isFinite(term) &&
    term > 0 &&
    !publish.isPending;

  const brandName = (id: string | null) =>
    brands.find((b) => b.id === id)?.name ?? id ?? "-";

  // Whether this actor may open a signature. A handwritten signature is a biometric artefact
  // that cannot be revoked and cannot be rotated, so the server holds a capability of its own
  // for it (AuthPolicies.SignaturePayload) and refuses a Manager outright. The button is
  // hidden as well, rather than opening a modal with nothing in it.
  const actorRole = user?.role ?? "";
  const canSeeSignature =
    actorRole === "Admin" || actorRole === "ProductOwner";

  return (
    <div className="space-y-6">
      {/* The provider's text: one document per provider, versioned. A new version changes the
          text for every contract of that provider; the old one is kept, because what a
          customer signed is a question the audit trail has to answer. */}
      <div className="glass-panel p-6">
        <h2 className="text-xl font-bold mb-1 flex items-center gap-2">
          <ScrollText className="w-5 h-5 text-primary" />
          {t("contracts.textsTitle")}
        </h2>
        <p className="text-sm text-muted-foreground mb-4">
          {t("contracts.textsHint")}
        </p>

        <div className="flex items-end gap-3 flex-wrap">
          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("contracts.provider")}
            </label>
            <Select value={brandId} onValueChange={setBrandId}>
              <SelectTrigger className="h-8 w-56">
                <SelectValue placeholder={t("contracts.selectProvider")} />
              </SelectTrigger>
              <SelectContent>
                {brands.map((b) => (
                  <SelectItem key={b.id} value={b.id}>
                    {b.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("contracts.termDays")}
            </label>
            <Input
              type="number"
              min={1}
              value={termDays}
              onChange={(e) => setTermDays(e.target.value)}
              className="h-8 w-28 text-right"
            />
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
              {t("contracts.document")}
            </label>
            <Input
              type="file"
              accept=".docx,.pdf"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null);
                e.target.value = "";
              }}
              className="h-8 w-72"
            />
          </div>

          <Button
            size="sm"
            className="h-8"
            onClick={() => publish.mutate()}
            disabled={!canPublish}
          >
            {publish.isPending ? (
              <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
            ) : (
              <FileUp className="w-3.5 h-3.5 mr-1" />
            )}
            {t("contracts.upload")}
          </Button>
        </div>

        {brandId && (
          <div className="mt-4">
            {versions.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                {t("contracts.noText")}
              </p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-muted">
                    <tr>
                      <th className="text-left p-3">
                        {t("contracts.versionNumber")}
                      </th>
                      <th className="text-left p-3">
                        {t("contracts.termDays")}
                      </th>
                      <th className="text-left p-3">
                        {t("contracts.publishedAt")}
                      </th>
                      <th className="text-left p-3">{t("contracts.text")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {versions.map((v) => (
                      <tr key={v.id} className="border-border">
                        <td className="p-3 font-mono">{v.version}</td>
                        <td className="p-3 tabular-nums">{v.termDays}</td>
                        <td className="p-3 text-muted-foreground">
                          {formatDate(v.createdAtUtc)}
                        </td>
                        <td className="p-3 text-muted-foreground">
                          <span className="line-clamp-2">{v.body}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Every contract on the platform. */}
      <div className="glass-panel p-6">
        <h2 className="text-xl font-bold mb-4">
          {t("contracts.availableTitle")}
        </h2>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-muted">
              <tr>
                <th className="text-left p-4">{t("contracts.provider")}</th>
                <th className="text-left p-4">{t("contracts.company")}</th>
                <th className="text-left p-4">{t("contracts.customer")}</th>
                <th className="text-left p-4">{t("contracts.termsVersion")}</th>
                <th className="text-left p-4">{t("contracts.validUntil")}</th>
                <th className="text-left p-4">{t("common.status")}</th>
                <th className="text-left p-4">{t("contracts.createdAt")}</th>
              </tr>
            </thead>
            <tbody>
              {contracts.map((contract) => (
                <tr key={contract.id} className="border-border">
                  <td className="p-4 font-bold">
                    {brandName(contract.brandId)}
                  </td>
                  <td className="p-4">{contract.companyName ?? "-"}</td>
                  <td className="p-4">{contract.customerName ?? "-"}</td>
                  <td className="p-4 font-mono">{contract.termsVersion}</td>
                  <td className="p-4 text-muted-foreground">
                    {formatDate(contract.expiresAtUtc)}
                  </td>
                  <td className="p-4">
                    {contract.signed ? (
                      <span className="px-2 py-1 rounded text-xs bg-success/20 text-success">
                        {t("contracts.signed")}
                      </span>
                    ) : (
                      <span className="px-2 py-1 rounded text-xs bg-muted text-muted-foreground">
                        {t("contracts.notSigned")}
                      </span>
                    )}
                    {contract.expired && (
                      <span className="ml-2 px-2 py-1 rounded text-xs bg-destructive/15 text-destructive">
                        {t("contracts.expired")}
                      </span>
                    )}
                  </td>
                  <td className="p-4 text-muted-foreground">
                    {formatDate(contract.createdAtUtc)}
                  </td>
                </tr>
              ))}
              {contracts.length === 0 && (
                <tr>
                  <td
                    colSpan={7}
                    className="p-8 text-center text-muted-foreground"
                  >
                    {t("contracts.noneAvailable")}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Every signature - a page of them, and the image is behind a capability. */}
      <div className="glass-panel p-6">
        <h2 className="text-xl font-bold mb-1">{t("contracts.signedTitle")}</h2>
        <p className="text-sm text-muted-foreground mb-4">
          {canSeeSignature
            ? t("contracts.signatureHint")
            : t("contracts.signatureRestricted")}
        </p>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead className="bg-muted">
              <tr>
                <th className="text-left p-4">{t("contracts.customer")}</th>
                <th className="text-left p-4">{t("contracts.company")}</th>
                <th className="text-left p-4">{t("contracts.provider")}</th>
                <th className="text-left p-4">{t("contracts.termsVersion")}</th>
                <th className="text-left p-4">{t("contracts.signedAt")}</th>
                <th className="text-left p-4">{t("contracts.validUntil")}</th>
                <th className="text-left p-4">{t("contracts.signature")}</th>
              </tr>
            </thead>
            <tbody>
              {signedContracts.map((sc) => (
                <tr key={sc.id} className="border-border">
                  <td className="p-4">{sc.customerName ?? "-"}</td>
                  <td className="p-4">{sc.companyName ?? "-"}</td>
                  <td className="p-4">{brandName(sc.brandId)}</td>
                  <td className="p-4 font-mono">{sc.termsVersion}</td>
                  <td className="p-4 text-muted-foreground">
                    {formatDateTime(sc.signedAtUtc)}
                  </td>
                  <td className="p-4 text-muted-foreground">
                    {sc.expiresAtUtc ? formatDate(sc.expiresAtUtc) : "-"}
                  </td>
                  <td className="p-4">
                    {canSeeSignature ? (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setSignatureId(sc.id)}
                      >
                        <FileSignature className="w-4 h-4 mr-2" />
                        {t("contracts.view")}
                      </Button>
                    ) : (
                      <span className="text-muted-foreground">-</span>
                    )}
                  </td>
                </tr>
              ))}
              {signedContracts.length === 0 && (
                <tr>
                  <td
                    colSpan={7}
                    className="p-8 text-center text-muted-foreground"
                  >
                    {t("contracts.noneSigned")}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>

        {signedTotalPages > 1 && (
          <div className="flex items-center justify-end gap-3 mt-4">
            <span className="text-sm text-muted-foreground">
              {t(
                "contracts.signaturesPage",
                String(signedPage),
                String(signedTotalPages),
              )}
            </span>
            <Button
              variant="outline"
              size="sm"
              disabled={signedPage <= 1}
              onClick={() => setSignedPage((p) => Math.max(1, p - 1))}
            >
              {"<"}
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={signedPage >= signedTotalPages}
              onClick={() => setSignedPage((p) => p + 1)}
            >
              {">"}
            </Button>
          </div>
        )}
      </div>

      {signatureId && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-md p-4"
          onClick={() => setSignatureId(null)}
        >
          <div
            className="glass-panel p-6 max-w-lg w-full animate-in zoom-in-50 duration-200"
            onClick={(e) => {
              e.stopPropagation();
            }}
          >
            <div className="flex justify-between items-center mb-4">
              <h3 className="text-xl font-bold text-foreground">
                {t("contracts.signature")}
              </h3>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setSignatureId(null)}
              >
                <X className="w-5 h-5" />
              </Button>
            </div>

            <div className="bg-white rounded-lg p-4 mb-6">
              {isSignatureLoading && (
                <p className="text-sm text-muted-foreground text-center">
                  {t("common.loading")}
                </p>
              )}
              {signature?.signatureData && (
                <SignatureViewer data={signature.signatureData} />
              )}
            </div>

            <Button
              className="w-full font-bold"
              onClick={() => setSignatureId(null)}
            >
              {t("common.close")}
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
