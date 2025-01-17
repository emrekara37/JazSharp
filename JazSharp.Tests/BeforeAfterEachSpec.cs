﻿namespace JazSharp.Tests
{
    public class BeforeAfterEachSpec : Spec
    {
        private string _orderedKey = string.Empty;

        public BeforeAfterEachSpec()
        {
            Describe("Before and after each", () =>
            {
                BeforeEach(() =>
                {
                    _orderedKey += "A";
                    Jaz.CurrentTest.Output.AppendLine("A");
                });

                BeforeEach(() =>
                {
                    _orderedKey += "B";
                    Jaz.CurrentTest.Output.AppendLine("B");
                });

                Describe("when scoped", () =>
                {
                    BeforeEach(() =>
                    {
                        _orderedKey += "C";
                        Jaz.CurrentTest.Output.AppendLine("C");
                    });

                    It("should run out-in and in-out respectively.", () =>
                    {
                    });

                    AfterEach(() =>
                    {
                        _orderedKey += "C";
                        Jaz.CurrentTest.Output.AppendLine("C");
                    });
                });

                Describe("(no tests, not run)", () =>
                {
                    BeforeEach(() =>
                    {
                        _orderedKey += "E";
                        Jaz.CurrentTest.Output.AppendLine("E");
                    });

                    AfterEach(() =>
                    {
                        _orderedKey += "E";
                        Jaz.CurrentTest.Output.AppendLine("E");
                    });
                });

                AfterEach(() =>
                {
                    _orderedKey += "B";
                    Jaz.CurrentTest.Output.AppendLine("B");
                });

                AfterEach(() =>
                {
                    _orderedKey += "A";
                    Jaz.CurrentTest.Output.AppendLine("A");
                    Expect(_orderedKey).ToBe("ABCCBA");
                });
            });
        }
    }
}
