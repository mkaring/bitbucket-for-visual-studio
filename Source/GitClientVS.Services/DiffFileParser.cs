﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GitClientVS.Contracts.Interfaces.Services;
using ParseDiff;

namespace GitClientVS.Services
{
    [Export(typeof(IDiffFileParser))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class DiffFileParser : IDiffFileParser
    { // from property holds the name of the file -> no matter what
        public IEnumerable<FileDiff> Parse(string diff)
        {
            var files = Diff.Parse(diff).ToList();

            foreach (var fileDiff in files)
            {
                if (fileDiff.Type == FileChangeType.Modified)
                {
                    foreach (var change in fileDiff.Chunks.Select(chunk => chunk.Changes).SelectMany(changes => changes))
                    {
                        if (change.Type == LineChangeType.Add)
                            change.NewIndex = change.Index;
                        else if (change.Type == LineChangeType.Delete)
                            change.OldIndex = change.Index;
                    }
                }
                else if (fileDiff.Type == FileChangeType.Add)
                {
                    fileDiff.From = fileDiff.To;

                    foreach (var change in fileDiff.Chunks.Select(chunk => chunk.Changes).SelectMany(changes => changes))
                        change.NewIndex = change.Index;
                }
                else if (fileDiff.Type == FileChangeType.Delete)
                {
                    foreach (var change in fileDiff.Chunks.Select(chunk => chunk.Changes).SelectMany(changes => changes))
                        change.OldIndex = change.Index;
                }

                yield return fileDiff;
            }
        }
    }
}
