import { useState, useMemo, Fragment } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  MessageSquare,
  Loader2,
  ChevronLeft,
  ChevronRight,
  Search,
  X,
  Trash2,
  CheckCheck,
  MessagesSquare,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ConfirmDialog } from "@/components/ui/alert-dialog";
import { toast } from "sonner";
import { apiRequest } from "@/lib/api-client";
import { formatDateTime, cn } from "@/lib/utils";

interface FeedbackItem {
  id: string;
  userId: string | null;
  telegramChatId: number;
  text: string;
  verdict: "Junk" | "Kept" | null;
  category: string | null;
  severity: string | null;
  summary: string | null;
  language: string | null;
  confidence: number | null;
  classifierError: string | null;
  status: "New" | "Triaged" | "Handled" | "Rejected";
  staffRepliedAtUtc: string | null;
  appVersion: string | null;
  platform: string | null;
  createdAtUtc: string;
  replyCount: number;
}

interface FeedbackReply {
  id: string;
  staffUserName: string | null;
  text: string;
  deliveryError: string | null;
  sentAtUtc: string;
}

interface FeedbackResponse {
  total: number;
  items: FeedbackItem[];
}

interface FeedbackFacets {
  categories: string[];
  statuses: string[];
  total: number;
  kept: number;
  junk: number;
  unclassified: number;
  replied: number;
}

const PAGE_SIZE = 50;

// The backlog opens on what the triage step decided is worth reading. "Junk" is one click away
// but not the default, because a default of "everything" puts "йоук" back at the top of the page
// and makes the classifier pointless for the person reading it.
type VerdictFilter = "kept" | "junk" | "unclassified" | "all";

function verdictParam(filter: VerdictFilter): string | undefined {
  if (filter === "kept") return "Kept";
  if (filter === "junk") return "Junk";
  if (filter === "all") return "";
  return undefined;
}

function severityBadge(severity: string | null): string {
  if (severity === "High") return "bg-destructive/15 text-destructive";
  if (severity === "Medium") return "bg-warning/15 text-warning";
  return "bg-muted text-muted-foreground";
}

function statusBadge(status: string): string {
  if (status === "Handled") return "bg-success/15 text-success";
  if (status === "Rejected") return "bg-muted text-muted-foreground";
  if (status === "Triaged") return "bg-primary/15 text-primary";
  return "bg-warning/15 text-warning";
}

export default function FeedbackTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState("");
  const [verdict, setVerdict] = useState<VerdictFilter>("kept");
  const [category, setCategory] = useState("all");
  const [status, setStatus] = useState("all");
  // Off by default: this is the "the classifier may be eating real complaints" lens, and a page
  // that opens on it would train everyone to ignore it.
  const [unsureOnly, setUnsureOnly] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [confirmDeleteJunk, setConfirmDeleteJunk] = useState(false);

  const { data: facets } = useQuery<FeedbackFacets>({
    queryKey: ["/api/admin/feedback/facets"],
    queryFn: () => apiRequest("GET", "/api/admin/feedback/facets"),
    staleTime: 60_000,
  });

  const filters = useMemo(() => {
    const params = new URLSearchParams({
      offset: String(page * PAGE_SIZE),
      limit: String(PAGE_SIZE),
    });
    if (search.trim()) params.set("search", search.trim());
    const v = verdictParam(verdict);
    if (v) params.set("verdict", v);
    if (category !== "all") params.set("category", category);
    if (status !== "all") params.set("status", status);
    if (unsureOnly) {
      params.set("verdict", "Junk");
      params.set("maxConfidence", "0.6");
    }
    return params.toString();
  }, [page, search, verdict, category, status, unsureOnly]);

  const { data, isLoading } = useQuery<FeedbackResponse>({
    queryKey: ["/api/admin/feedback", filters],
    queryFn: () => apiRequest("GET", `/api/admin/feedback?${filters}`),
  });

  const { data: detail } = useQuery<{
    item: FeedbackItem;
    replies: FeedbackReply[];
  }>({
    queryKey: ["/api/admin/feedback", expanded],
    queryFn: () => apiRequest("GET", `/api/admin/feedback/${expanded}`),
    enabled: !!expanded,
  });

  // One call for a single row and for the whole page: the server ignores ids already in the
  // requested state, so a double-click cannot rewrite who decided what.
  const statusMutation = useMutation({
    mutationFn: ({ ids, status }: { ids: string[]; status: string }) =>
      apiRequest("POST", "/api/admin/feedback/status", { ids, status }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/feedback"] });
      toast.success(t("feedback.stateChanged"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const deleteJunkMutation = useMutation({
    mutationFn: () => apiRequest("DELETE", "/api/admin/feedback?verdict=Junk"),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/feedback"] });
      setConfirmDeleteJunk(false);
      toast.success(t("feedback.junkCleared"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 0;
  const listed = data?.items ?? [];
  const hasFilters =
    search.trim() !== "" ||
    verdict !== "kept" ||
    category !== "all" ||
    status !== "all" ||
    unsureOnly;

  const resetFilters = () => {
    setSearch("");
    setVerdict("kept");
    setCategory("all");
    setStatus("all");
    setUnsureOnly(false);
    setPage(0);
  };

  const markListedHandled = () =>
    statusMutation.mutate({
      ids: listed.filter((i) => i.status !== "Handled").map((i) => i.id),
      status: "Handled",
    });

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative flex-1 min-w-48">
          <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(0);
            }}
            placeholder={t("common.search")}
            className="pl-9 h-9"
          />
        </div>
        <Select
          value={verdict}
          onValueChange={(v) => {
            setVerdict(v as VerdictFilter);
            setUnsureOnly(false);
            setPage(0);
          }}
        >
          <SelectTrigger className="h-9 w-44" title={t("feedback.verdictHint")}>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="kept">{t("feedback.verdictKept")}</SelectItem>
            <SelectItem value="junk">{t("feedback.verdictJunk")}</SelectItem>
            <SelectItem value="unclassified">
              {t("feedback.verdictUnclassified")}
            </SelectItem>
            <SelectItem value="all">{t("feedback.verdictAll")}</SelectItem>
          </SelectContent>
        </Select>
        <Select
          value={category}
          onValueChange={(v) => {
            setCategory(v);
            setPage(0);
          }}
        >
          <SelectTrigger className="h-9 w-36">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">
              {t("feedback.category")}: {t("common.all")}
            </SelectItem>
            {(facets?.categories ?? []).map((c) => (
              <SelectItem key={c} value={c}>
                {c}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v);
            setPage(0);
          }}
        >
          <SelectTrigger className="h-9 w-40">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">
              {t("feedback.status")}: {t("common.all")}
            </SelectItem>
            {(facets?.statuses ?? []).map((s) => (
              <SelectItem key={s} value={s}>
                {t(`feedback.status.${s}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button
          variant={unsureOnly ? "default" : "outline"}
          size="sm"
          onClick={() => {
            setUnsureOnly((v) => !v);
            setPage(0);
          }}
          className="h-9"
          title={t("feedback.unsureHint")}
        >
          <MessagesSquare className="w-4 h-4 mr-1" />
          {t("feedback.unsure")}
        </Button>
        {hasFilters && (
          <Button
            variant="ghost"
            size="sm"
            onClick={resetFilters}
            className="h-9"
          >
            <X className="w-4 h-4 mr-1" />
            {t("common.close")}
          </Button>
        )}
        <Button
          variant="outline"
          size="sm"
          onClick={markListedHandled}
          disabled={
            statusMutation.isPending ||
            !listed.some((i) => i.status !== "Handled")
          }
          className="h-9"
        >
          <CheckCheck className="w-4 h-4 mr-1" />
          {t("feedback.markHandled")}
        </Button>
        <Button
          variant="destructive"
          size="sm"
          onClick={() => setConfirmDeleteJunk(true)}
          disabled={deleteJunkMutation.isPending || !facets?.junk}
          className="h-9"
        >
          <Trash2 className="w-4 h-4 mr-1" />
          {t("feedback.clearJunk")}
        </Button>
      </div>

      {facets && (
        <p className="text-xs text-muted-foreground">
          {t(
            "feedback.summary",
            String(facets.total),
            String(facets.kept),
            String(facets.junk),
            String(facets.unclassified),
            String(facets.replied),
          )}
        </p>
      )}

      {isLoading ? (
        <div className="flex items-center justify-center py-20">
          <Loader2 className="w-8 h-8 animate-spin text-primary" />
        </div>
      ) : !listed.length ? (
        <div className="text-center py-20 text-muted-foreground">
          <MessageSquare className="w-12 h-12 mx-auto mb-4 opacity-40" />
          <p>{t("feedback.empty")}</p>
        </div>
      ) : (
        <>
          <div className="bg-card border border-border rounded-lg overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border bg-muted/50">
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">
                      {t("feedback.date")}
                    </th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">
                      {t("feedback.category")}
                    </th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">
                      {t("feedback.message")}
                    </th>
                    <th className="text-left p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider">
                      {t("feedback.status")}
                    </th>
                    <th className="text-right p-3 font-medium text-muted-foreground text-xs uppercase tracking-wider"></th>
                  </tr>
                </thead>
                <tbody>
                  {listed.map((item) => (
                    <Fragment key={item.id}>
                      <tr className="border-b border-border/60 hover:bg-muted/30">
                        <td className="p-3 align-top whitespace-nowrap text-muted-foreground text-xs">
                          {formatDateTime(item.createdAtUtc)}
                        </td>
                        <td className="p-3 align-top">
                          <div className="flex flex-wrap gap-1">
                            {item.category && (
                              <span className="rounded-md px-2 py-0.5 text-xs bg-primary/10 text-primary">
                                {item.category}
                              </span>
                            )}
                            {item.severity && (
                              <span
                                className={cn(
                                  "rounded-md px-2 py-0.5 text-xs",
                                  severityBadge(item.severity),
                                )}
                              >
                                {item.severity}
                              </span>
                            )}
                            {item.verdict === "Junk" && (
                              <span className="rounded-md px-2 py-0.5 text-xs bg-muted text-muted-foreground">
                                {t("feedback.verdictJunk")}
                                {item.confidence != null &&
                                  ` (${Math.round(item.confidence * 100)}%)`}
                              </span>
                            )}
                            {item.verdict == null && (
                              <span className="rounded-md px-2 py-0.5 text-xs bg-warning/15 text-warning">
                                {t("feedback.verdictUnclassified")}
                              </span>
                            )}
                          </div>
                          {(item.appVersion || item.platform) && (
                            <p className="mt-1 text-xs text-muted-foreground">
                              {item.platform} {item.appVersion}
                            </p>
                          )}
                        </td>
                        <td className="p-3 align-top">
                          {item.summary && (
                            <p className="font-medium">{item.summary}</p>
                          )}
                          <p className="text-muted-foreground whitespace-pre-wrap break-words">
                            {item.text}
                          </p>
                          {item.classifierError && (
                            <p className="mt-1 text-xs text-destructive">
                              {item.classifierError}
                            </p>
                          )}
                        </td>
                        <td className="p-3 align-top">
                          <span
                            className={cn(
                              "rounded-md px-2 py-0.5 text-xs",
                              statusBadge(item.status),
                            )}
                          >
                            {t(`feedback.status.${item.status}`)}
                          </span>
                          {item.staffRepliedAtUtc && (
                            <p className="mt-1 text-xs text-muted-foreground">
                              {t("feedback.answered")}
                            </p>
                          )}
                        </td>
                        <td className="p-3 align-top text-right whitespace-nowrap">
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() =>
                              setExpanded(expanded === item.id ? null : item.id)
                            }
                            className="h-8"
                          >
                            {t("feedback.details")}
                          </Button>
                        </td>
                      </tr>
                      {expanded === item.id && (
                        <tr className="border-b border-border/60 bg-muted/20">
                          <td colSpan={5} className="p-4 text-sm">
                            <dl className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-3 text-xs">
                              <div>
                                <dt className="text-muted-foreground">
                                  {t("feedback.language")}
                                </dt>
                                <dd>{item.language ?? "—"}</dd>
                              </div>
                              <div>
                                <dt className="text-muted-foreground">
                                  {t("feedback.confidence")}
                                </dt>
                                <dd>
                                  {item.confidence != null
                                    ? Math.round(item.confidence * 100) + "%"
                                    : "—"}
                                </dd>
                              </div>
                              <div>
                                <dt className="text-muted-foreground">
                                  {t("feedback.user")}
                                </dt>
                                <dd className="font-mono">
                                  {item.userId ?? t("feedback.anonymous")}
                                </dd>
                              </div>
                              <div>
                                <dt className="text-muted-foreground">
                                  {t("feedback.telegramChat")}
                                </dt>
                                <dd className="font-mono">
                                  {item.telegramChatId}
                                </dd>
                              </div>
                            </dl>
                            <h4 className="font-medium mb-1">
                              {t("feedback.replies")}
                            </h4>
                            {!detail?.replies.length ? (
                              <p className="text-muted-foreground text-xs">
                                {t("feedback.noReplies")}
                              </p>
                            ) : (
                              <ul className="space-y-2">
                                {detail.replies.map((reply) => (
                                  <li
                                    key={reply.id}
                                    className="border-l-2 border-border pl-3"
                                  >
                                    <p className="whitespace-pre-wrap break-words">
                                      {reply.text}
                                    </p>
                                    <p className="text-xs text-muted-foreground">
                                      {reply.staffUserName} ·{" "}
                                      {formatDateTime(reply.sentAtUtc)}
                                      {reply.deliveryError &&
                                        ` · ${reply.deliveryError}`}
                                    </p>
                                  </li>
                                ))}
                              </ul>
                            )}
                            <div className="flex gap-2 mt-4">
                              <Button
                                size="sm"
                                variant="outline"
                                disabled={statusMutation.isPending}
                                onClick={() =>
                                  statusMutation.mutate({
                                    ids: [item.id],
                                    status: "Handled",
                                  })
                                }
                              >
                                {t("feedback.markHandled")}
                              </Button>
                              <Button
                                size="sm"
                                variant="outline"
                                disabled={statusMutation.isPending}
                                onClick={() =>
                                  statusMutation.mutate({
                                    ids: [item.id],
                                    status: "Rejected",
                                  })
                                }
                              >
                                {t("feedback.markRejected")}
                              </Button>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div className="flex items-center justify-between">
            <p className="text-sm text-muted-foreground">
              {t(
                "feedback.showing",
                String(page * PAGE_SIZE + 1),
                String(Math.min((page + 1) * PAGE_SIZE, data?.total ?? 0)),
                String(data?.total ?? 0),
              )}
            </p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={page === 0}
                onClick={() => setPage((p) => p - 1)}
              >
                <ChevronLeft className="w-4 h-4" />
              </Button>
              <span className="flex items-center text-sm text-muted-foreground">
                {page + 1} / {Math.max(totalPages, 1)}
              </span>
              <Button
                variant="outline"
                size="sm"
                disabled={page + 1 >= totalPages}
                onClick={() => setPage((p) => p + 1)}
              >
                <ChevronRight className="w-4 h-4" />
              </Button>
            </div>
          </div>
        </>
      )}

      <ConfirmDialog
        open={confirmDeleteJunk}
        onOpenChange={setConfirmDeleteJunk}
        destructive
        title={t("feedback.clearJunk")}
        description={t("feedback.clearJunkConfirm")}
        confirmLabel={t("feedback.clearJunk")}
        cancelLabel={t("common.cancel")}
        onConfirm={() => deleteJunkMutation.mutate()}
      />
    </div>
  );
}
