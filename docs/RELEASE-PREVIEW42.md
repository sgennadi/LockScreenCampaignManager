# LockScreen Campaign Manager 0.4.0-preview.42

## Wizard progress fix
- Correct a state-machine regression that left the editor on **STEP 1 OF 6 — Connect to AD** even after images had been imported and a schedule configured through the editor toolbar.
- Importing images while Wizard is active now takes you directly to **Arrange images**. Editing and accepting a schedule now advances to **Validate & Save**, whether started from a Wizard button or from the image toolbar.
- Selecting Wizard after you have already imported images resumes at the first incomplete step rather than resetting to AD sign-in.
- The **Save & continue** button cannot bypass saving a local project. The final GPO deployment or Finish offline choice is offered only after project saving succeeds. Canceling the save dialog keeps the user in the review step.
- AD sign-in remains optional for locally prepared campaigns. GPO deployment still requires authorized AD credentials and selected OU/group permissions.
- Add source-level regression assertions for step transitions and the save requirement.

## Compatibility
Windows 10 and Windows 11 x64/x86. GitHub CI builds portable EXEs and runs offline tests; a real domain-connected pilot remains necessary before production GPO deployment.
