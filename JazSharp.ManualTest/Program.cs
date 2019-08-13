﻿using JazSharp.Testing;
using Mono.Cecil;

namespace JazSharp.ManualTest
{
    class Program
    {
        static void Main(string[] _)
        {
            var info = AssemblyDefinition.ReadAssembly(System.Reflection.Assembly.GetExecutingAssembly().Location).MainModule.GetType("JazSharp.ManualTest.Program").Methods;

            using (var testCollection = TestCollection.FromSources(new[] { @"C:\Users\seamillo\source\repos\JazSharp\JazSharp.Tests\bin\Debug\netcoreapp2.2\JazSharp.Tests.dll" }))
            using (var testRun = testCollection.CreateTestRun())
            {
                var result = testRun.ExecuteAsync().Result;
            }
        }

        public void Test<T>(T p, ref T p2, out T p3)
        {
            var data = new object[] { p, p2, default(T) };
            p = (T)data[0];
            p2 = (T)data[1];
            p3 = (T)data[2];
        }
    }
}