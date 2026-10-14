// The shared test sources compile against either driver: the W.DamengProvider test projects
// link these files and define WDRIVER. Both drivers expose the same ADO.NET type names.
#if WDRIVER
global using W.Dm;
#else
global using Dm;
#endif
