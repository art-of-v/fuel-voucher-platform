import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import AdminLogin from "./AdminLogin";
import { sendCode, signInWithCode } from "@/lib/session";
import { useSession } from "@/lib/session-store";

vi.mock("@/lib/session", () => ({
  sendCode: vi.fn().mockResolvedValue(undefined),
  signInWithCode: vi.fn(),
  signInWithTwoFactor: vi.fn(),
}));

const mockedSendCode = vi.mocked(sendCode);
const mockedSignInWithCode = vi.mocked(signInWithCode);

/** Walks the form from the phone step to the second-factor one, and hands back the
 *  authenticator-code field together with the user-event handle for whatever follows. */
async function arriveAtSecondFactor() {
  const user = userEvent.setup();
  render(<AdminLogin />);

  await user.type(screen.getByPlaceholderText("+380XXXXXXXXX"), "501234567");
  await user.click(screen.getByRole("button", { name: "Send Code" }));

  await user.type(await screen.findByPlaceholderText("000000"), "123456");
  await user.click(screen.getByRole("button", { name: "Verify Code" }));

  await screen.findByText("Step 2 of 2 — the authenticator code");
  return { user, field: screen.getByPlaceholderText("000000") };
}

describe("AdminLogin", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockedSendCode.mockResolvedValue(undefined);
  });

  it("unlocks the second code's button after the first code is accepted", async () => {
    // The defect this pins: the two_factor branch of handleVerifyCode returned before the
    // trailing setLoginLoading(false), so the spinner survived the step change and the
    // second code's Verify button stayed disabled no matter what was typed (planning
    // scenario N2, found on the panel's first enrolled sign-in).
    mockedSignInWithCode.mockResolvedValue({
      status: "two_factor",
      challenge: "challenge-1",
      needsEnrollment: false,
    });

    const { user, field } = await arriveAtSecondFactor();

    await user.type(field, "654321");
    expect(screen.getByRole("button", { name: "Verify Code" })).toBeEnabled();
  });

  it("announces the second entry as step 2, and shows the enrolment key when there is none yet", async () => {
    mockedSignInWithCode.mockResolvedValue({
      status: "two_factor",
      challenge: "challenge-1",
      needsEnrollment: true,
      enrollmentSecret: "JBSWY3DPEHPK3PXP",
    });

    const { user, field } = await arriveAtSecondFactor();

    expect(screen.getByText("JBSWY3DPEHPK3PXP")).toBeInTheDocument();
    await user.type(field, "654321");
    expect(screen.getByRole("button", { name: "Verify Code" })).toBeEnabled();
  });

  it("says why the session ended when the idle lock signed the operator out", () => {
    // A lock that returns the operator to a plain login form reads like a crash. The store knows
    // the reason, so the screen says it; a deliberate sign-out leaves it blank.
    useSession.setState({ status: "anonymous", lastExit: "idle" });
    render(<AdminLogin />);

    expect(screen.getByRole("status")).toHaveTextContent(
      /signed out after 15 minutes without activity/i,
    );
  });

  it("does not blame idleness for a deliberate sign-out", () => {
    useSession.setState({ status: "anonymous", lastExit: null });
    render(<AdminLogin />);

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });
});
