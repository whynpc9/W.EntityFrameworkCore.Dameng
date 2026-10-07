#!/usr/bin/env python3
"""扫描固定 Git 提交的 C# 聚合候选，输出定位清单；不推断 IQueryable 类型。"""
import argparse
import bisect
import collections
import re
import subprocess

OPERATORS = "GroupBy|Sum|SumAsync|Average|AverageAsync|Min|MinAsync|Max|MaxAsync|LongCount|LongCountAsync|Count|CountAsync|Any|AnyAsync|All|AllAsync|Aggregate"
METHOD_PATTERN = re.compile(r"\.(" + OPERATORS + r")\s*\(")
# 查询语法仅做词法候选定位；注释/字符串保持原始偏移，不作为关键字。
TOKEN_PATTERN = re.compile(
    r'//[^\n]*|/\*.*?\*/|(?P<rawquotes>"{3,}).*?(?P=rawquotes)|'
    r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'
    r"'(?:\\.|[^'\\])*'|@?\w+|[^\s]",
    re.DOTALL,
)
# git grep 按行筛选，这里只筛选词，不要求同一行包含完整查询子句或左括号。
PREFILTER = r"\.(" + OPERATORS + r")([^[:alnum:]_]|$)|(^|[^[:alnum:]_])group([^[:alnum:]_]|$)"


def query_group_starts(source):
    """逐个检查 group 候选，不能让早期候选吞掉后面的查询子句。"""
    tokens = [(match.group(), match.start()) for match in TOKEN_PATTERN.finditer(source)
              if not match.group().startswith(("//", "/*"))]
    for index, (value, start) in enumerate(tokens):
        if value != "group" or index + 1 == len(tokens):
            continue
        previous = tokens[index - 1][0] if index else None
        following = tokens[index + 1][0]
        if previous in (".", "var", "in", "into", "from", "let", "join", "=", "class", "struct", "namespace"):
            continue
        if following in ("=", ".", ";", ",", ")", "]", "}", "in"):
            continue
        stack = []
        seen_element = False
        for cursor in range(index + 1, len(tokens)):
            token = tokens[cursor][0]
            previous = tokens[cursor - 1][0]
            if not stack:
                if token in (";", ",", ")", "]", "}"):
                    break
                if token == "by" and seen_element and previous != ".":
                    yield start
                    break
                # group 也可以是元素变量名/成员名；只有表达式后的新子句才截断。
                if token == "group" and seen_element and previous not in (".", "+", "-", "*", "/", "%", "?", ":", "&", "|", "^", "!", "=", "<", ">"):
                    break
            if token in ("(", "[", "{"):
                stack.append({"(": ")", "[": "]", "{": "}"}[token])
            elif token in (")", "]", "}"):
                if not stack or stack.pop() != token:
                    break
            seen_element = True


def find_hits(source):
    """跨行扫描，在 group 关键字或聚合方法名所在行记录候选。"""
    newlines = [position for position, char in enumerate(source) if char == "\n"]
    hits = collections.defaultdict(set)
    for match in METHOD_PATTERN.finditer(source):
        hits[match.group(1)].add(bisect.bisect_right(newlines, match.start()) + 1)
    for start in query_group_starts(source):
        hits["query_group"].add(bisect.bisect_right(newlines, start) + 1)
    return {operator: sorted(lines) for operator, lines in hits.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repository")
    parser.add_argument("--ref", default="origin/master")
    args = parser.parse_args()

    def git(*command):
        return subprocess.check_output(["git", "-C", args.repository, *command], text=True)

    commit = git("rev-parse", args.ref).strip()
    result = subprocess.run(
        ["git", "-C", args.repository, "grep", "-l", "-E", PREFILTER, commit, "--", "*.cs"],
        text=True, capture_output=True,
    )
    # git grep 的 1 表示没有候选；其他非零退出仍是扫描失败。
    if result.returncode not in (0, 1):
        raise subprocess.CalledProcessError(result.returncode, result.args, result.stdout, result.stderr)
    files = [path.removeprefix(commit + ":") for path in result.stdout.splitlines()]
    print("# commit=" + commit)
    print("# 静态候选：须人工追踪 DbSet/IQueryable、物化边界和继承方法；Math/Enumerable/MongoDB 也可能命中。")
    print("file\toperators:lines")
    for path in sorted(files):
        hits = find_hits(git("show", commit + ":" + path))
        if hits:
            print(path + "\t" + ";".join(key + ":" + ",".join(map(str, hits[key])) for key in sorted(hits)))


if __name__ == "__main__":
    main()
