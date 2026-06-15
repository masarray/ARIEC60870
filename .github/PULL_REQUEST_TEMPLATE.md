## Summary

Describe the change in user-facing terms.

## Type of change

- [ ] Protocol decoding / communication behavior
- [ ] Desktop UI / UX
- [ ] Evidence export / report
- [ ] Documentation / SEO / landing page
- [ ] Release automation / CI
- [ ] Tests / test vectors

## Clean-room and license check

- [ ] I did not copy source code from commercial, GPL, or unclear-license IEC 60870 stacks.
- [ ] New files have compatible licensing or attribution.
- [ ] Public examples and traces are sanitized.

## Validation

- [ ] `dotnet restore ARIEC60870.sln`
- [ ] `dotnet build ARIEC60870.sln --configuration Release`
- [ ] `dotnet run --project tests/ARIEC60870.Protocol.Tests/ARIEC60870.Protocol.Tests.csproj --configuration Release`
- [ ] Manual UI check, if applicable

## Notes for reviewers

Add screenshots, sanitized frames, or release workflow notes when helpful.
