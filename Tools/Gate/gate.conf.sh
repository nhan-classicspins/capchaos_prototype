# Tools/Gate/gate.conf.sh — this SKU's settings for the framework's Tools/pf/gate.sh.
#
# SKU-OWNED (emitted once by the Setup Wizard's test kit). Sourced by Tools/pf/gate.sh before any leg
# runs; plain POSIX sh, no side effects beyond setting the names below. Run the gate with
# `sh Tools/pf/gate.sh`.

TEST_PROJECT="SkuHeadlessTests/CapsChaos.SkuHeadlessTests.csproj"

# --- Leg 4 (optional): ONE [Slow] test the gate still owns, keyed on its inputs. -----------------
# A Slow test is excluded from leg 3 by `TestCategory!=Slow`. When one of them guards something the
# gate must not lose — a generator's byte-for-byte determinism, say — name it here: the gate runs it
# BY NAME (never the whole Slow set) whenever the hash of keyed_test_inputs' files differs from the
# last green run's, and reports it REUSED otherwise. Under-scoping the input list is the one way this
# goes quietly wrong, so err wide.
#
# KEYED_TEST="The_generated_block_regenerates_byte_identically_from_its_recorded_seed"
# KEYED_TEST_LABEL="determinism"
# KEYED_TEST_TITLE="generator determinism"
# KEYED_TEST_STAMP="determinism"
# KEYED_TEST_ENV="PF_RUN_SLOW=1"
#
# keyed_test_inputs() {
#     fw=$(framework_root)
#     find "$ROOT/Assets/CapsChaos/Domain" "$ROOT/Assets/CapsChaos/Gen" -type f -name '*.cs' 2>/dev/null
#     [ -n "$fw" ] && find "$fw/Runtime/Domain" -type f -name '*.cs' 2>/dev/null
#     [ -n "$fw" ] || echo "no-framework-root"
# }
