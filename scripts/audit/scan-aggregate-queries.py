#!/usr/bin/env python3
"""扫描固定 Git 提交的 C# 聚合候选，输出定位清单；不推断 IQueryable 类型。"""
import argparse
import collections
import re
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("repository")
parser.add_argument("--ref", default="origin/master")
args = parser.parse_args()

def git(*command):
    return subprocess.check_output(["git", "-C", args.repository, *command], text=True)

commit = git("rev-parse", args.ref).strip()
# Count/Any 同步重载单独列出，避免把大量内存集合计数算成已确认的 EF 查询。
pattern = r"\.(GroupBy|Sum|SumAsync|Average|AverageAsync|Min|MinAsync|Max|MaxAsync|LongCount|LongCountAsync|Count|CountAsync|Any|AnyAsync|All|AllAsync|Aggregate)\s*\("
query_pattern = r"\bgroup\s+.+\s+by\b"
files = git("grep", "-l", "-E", r"\.(GroupBy|Sum|SumAsync|Average|AverageAsync|Min|MinAsync|Max|MaxAsync|LongCount|LongCountAsync|Count|CountAsync|Any|AnyAsync|All|AllAsync|Aggregate)[[:space:]]*\(|group[[:space:]]+.+[[:space:]]+by", commit, "--", "*.cs").splitlines()
files = [path.removeprefix(commit + ":") for path in files]
print("# commit=" + commit)
print("# 静态候选：须人工追踪 DbSet/IQueryable、物化边界和继承方法；Math/Enumerable/MongoDB 也可能命中。")
print("file\toperators:lines")
for path in files:
    if not path.endswith(".cs"):
        continue
    source = git("show", commit + ":" + path)
    hits = collections.defaultdict(list)
    for number, line in enumerate(source.splitlines(), 1):
        for operator in set(re.findall(pattern, line)):
            hits[operator].append(number)
        if re.search(query_pattern, line):
            hits["query_group"].append(number)
    if hits:
        print(path + "\t" + ";".join(key + ":" + ",".join(map(str, hits[key])) for key in sorted(hits)))
